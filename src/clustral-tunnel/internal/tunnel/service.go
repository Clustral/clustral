package tunnel

import (
	"context"
	"io"
	"log/slog"

	pb "clustral-tunnel/gen/clustral/v1"
	"clustral-tunnel/internal/auth"

	"go.mongodb.org/mongo-driver/v2/bson"
	"go.mongodb.org/mongo-driver/v2/mongo"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/status"
	"google.golang.org/protobuf/types/known/timestamppb"
)

var version = "0.0.0-dev"

// TunnelServiceServer implements the TunnelService gRPC server.
type TunnelServiceServer struct {
	pb.UnimplementedTunnelServiceServer
	manager *SessionManager
	mongoDB *mongo.Database
	logger  *slog.Logger
}

// NewTunnelServiceServer creates a new TunnelServiceServer.
func NewTunnelServiceServer(manager *SessionManager, db *mongo.Database, logger *slog.Logger) *TunnelServiceServer {
	return &TunnelServiceServer{
		manager: manager,
		mongoDB: db,
		logger:  logger,
	}
}

// OpenTunnel handles a bidirectional agent tunnel stream.
func (s *TunnelServiceServer) OpenTunnel(stream pb.TunnelService_OpenTunnelServer) error {
	ctx := stream.Context()

	// Extract cluster ID from validated auth context.
	identity, ok := auth.GetAgentIdentity(ctx)
	if !ok {
		return status.Error(codes.Unauthenticated, "agent authentication required")
	}
	clusterID := identity.ClusterID

	// 1. Handshake: expect AgentHello as first message.
	firstMsg, err := stream.Recv()
	if err != nil {
		return status.Errorf(codes.Canceled, "stream closed before handshake: %v", err)
	}

	hello := firstMsg.GetHello()
	if hello == nil {
		return status.Error(codes.InvalidArgument, "expected AgentHello as the first message")
	}

	s.logger.Info("Agent tunnel opened",
		"clusterId", clusterID,
		"agentVersion", hello.AgentVersion,
		"k8sVersion", hello.KubernetesVersion)

	// 2. Send TunnelHello.
	session := NewSession(clusterID, stream)
	if err := session.SendHello(&pb.TunnelHello{
		ClusterId:  clusterID,
		ServerTime: timestamppb.Now(),
	}); err != nil {
		return status.Errorf(codes.Internal, "failed to send TunnelHello: %v", err)
	}

	// 3. Update cluster status to Connected in MongoDB.
	clusters := s.mongoDB.Collection("clusters")
	_, err = clusters.UpdateOne(ctx,
		bson.M{"_id": clusterID},
		bson.M{"$set": bson.M{
			"Status":            "Connected",
			"LastSeenAt":        bson.M{"$date": nil}, // will be set by MongoDB
			"KubernetesVersion": hello.KubernetesVersion,
			"AgentVersion":      hello.AgentVersion,
		}},
	)
	if err != nil {
		s.logger.Warn("Failed to update cluster status", "clusterId", clusterID, "error", err)
	}

	// Version compatibility check.
	if hello.AgentVersion != "" && hello.AgentVersion != version {
		s.logger.Warn("Agent version mismatch",
			"clusterId", clusterID,
			"agentVersion", hello.AgentVersion,
			"tunnelVersion", version)
	}

	// 4. Register session.
	if err := s.manager.Register(ctx, clusterID, session); err != nil {
		s.logger.Warn("Failed to register session in Redis", "clusterId", clusterID, "error", err)
	}

	defer func() {
		// 6. Agent disconnected — mark as Disconnected.
		s.logger.Info("Agent tunnel closed", "clusterId", clusterID)
		_, _ = clusters.UpdateOne(context.Background(),
			bson.M{"_id": clusterID},
			bson.M{"$set": bson.M{"Status": "Disconnected"}},
		)
		_ = s.manager.Unregister(context.Background(), clusterID)
	}()

	// 5. Dispatch loop: process incoming frames.
	for {
		msg, err := stream.Recv()
		if err != nil {
			if err == io.EOF {
				return nil
			}
			return err
		}

		// Update last-seen on every message.
		_, _ = clusters.UpdateOne(ctx,
			bson.M{"_id": clusterID},
			bson.M{"$set": bson.M{"LastSeenAt": timestamppb.Now().AsTime()}},
		)

		switch p := msg.Payload.(type) {
		case *pb.TunnelClientMessage_HttpResponse:
			session.HandleHttpResponse(p.HttpResponse)

		case *pb.TunnelClientMessage_Ping:
			if err := session.SendPong(p.Ping); err != nil {
				s.logger.Warn("Failed to send pong", "clusterId", clusterID, "error", err)
			}

		case *pb.TunnelClientMessage_Pong:
			// RTT measurement — no-op for now.

		default:
			s.logger.Warn("Unexpected payload type from agent, ignoring",
				"clusterId", clusterID)
		}
	}
}
