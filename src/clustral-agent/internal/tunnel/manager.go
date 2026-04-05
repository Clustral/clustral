package tunnel

import (
	"context"
	"fmt"
	"io"
	"log/slog"
	"math/rand"
	"strings"
	"sync"
	"time"

	pb "github.com/Clustral/clustral/src/clustral-agent/gen/clustralv1"
	"github.com/Clustral/clustral/src/clustral-agent/internal/config"
	"github.com/Clustral/clustral/src/clustral-agent/internal/credential"
	"github.com/Clustral/clustral/src/clustral-agent/internal/proxy"
	"golang.org/x/sync/errgroup"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/status"
	"google.golang.org/protobuf/types/known/timestamppb"
)

type Manager struct {
	cfg    *config.Config
	creds  *credential.Store
	proxy  *proxy.Proxy
	logger *slog.Logger
}

func NewManager(cfg *config.Config, creds *credential.Store, p *proxy.Proxy, logger *slog.Logger) *Manager {
	return &Manager{cfg: cfg, creds: creds, proxy: p, logger: logger}
}

func (m *Manager) Run(ctx context.Context) {
	delay := m.cfg.ReconnectInitialDelay

	for {
		select {
		case <-ctx.Done():
			m.logger.Info("Tunnel shutdown requested")
			return
		default:
		}

		m.logger.Info("Connecting to ControlPlane", "url", m.cfg.ControlPlaneURL)

		err := m.connectAndRun(ctx)

		if ctx.Err() != nil {
			return
		}

		if err == nil {
			m.logger.Info("Tunnel closed cleanly, reconnecting immediately")
			delay = m.cfg.ReconnectInitialDelay
			continue
		}

		if s, ok := status.FromError(err); ok && s.Code() == codes.Unauthenticated {
			m.logger.Error("Authentication rejected — check agent credential")
			delay = m.cfg.ReconnectMaxDelay
		} else {
			m.logger.Warn("Tunnel error, reconnecting", "delay", delay, "error", err)
		}

		jitter := time.Duration(rand.Float64() * float64(m.cfg.ReconnectMaxJitter))
		select {
		case <-ctx.Done():
			return
		case <-time.After(delay + jitter):
		}

		delay = time.Duration(float64(delay) * m.cfg.ReconnectBackoffMultiplier)
		if delay > m.cfg.ReconnectMaxDelay {
			delay = m.cfg.ReconnectMaxDelay
		}
	}
}

func (m *Manager) connectAndRun(ctx context.Context) error {
	token, err := m.creds.ReadToken()
	if err != nil || token == "" {
		return fmt.Errorf("no agent credential found")
	}

	// Create gRPC connection.
	opts := []grpc.DialOption{
		grpc.WithTransportCredentials(insecure.NewCredentials()),
	}

	addr := strings.TrimPrefix(strings.TrimPrefix(m.cfg.ControlPlaneURL, "http://"), "https://")
	conn, err := grpc.NewClient(addr, opts...)
	if err != nil {
		return fmt.Errorf("gRPC dial: %w", err)
	}
	defer conn.Close()

	// Attach bearer token as metadata.
	md := metadata.Pairs("authorization", "Bearer "+token)
	streamCtx := metadata.NewOutgoingContext(ctx, md)

	tunnelClient := pb.NewTunnelServiceClient(conn)
	clusterClient := pb.NewClusterServiceClient(conn)

	stream, err := tunnelClient.OpenTunnel(streamCtx)
	if err != nil {
		return fmt.Errorf("open tunnel: %w", err)
	}

	// Handshake: send AgentHello.
	if err := stream.Send(&pb.TunnelClientMessage{
		Payload: &pb.TunnelClientMessage_Hello{
			Hello: &pb.AgentHello{
				ClusterId:         m.cfg.ClusterID,
				AgentVersion:      m.cfg.AgentVersion,
				KubernetesVersion: "",
				SentAt:            timestamppb.Now(),
			},
		},
	}); err != nil {
		return fmt.Errorf("send AgentHello: %w", err)
	}

	// Wait for TunnelHello.
	msg, err := stream.Recv()
	if err != nil {
		return fmt.Errorf("recv TunnelHello: %w", err)
	}
	hello := msg.GetHello()
	if hello == nil {
		return fmt.Errorf("expected TunnelHello, got %T", msg.GetPayload())
	}

	m.logger.Info("Tunnel established", "clusterId", hello.ClusterId)

	// Run dispatch + heartbeat concurrently.
	var writeMu sync.Mutex
	g, gCtx := errgroup.WithContext(ctx)

	g.Go(func() error {
		return m.dispatchFrames(gCtx, stream, &writeMu)
	})

	g.Go(func() error {
		return m.heartbeat(gCtx, clusterClient)
	})

	return g.Wait()
}

func (m *Manager) dispatchFrames(ctx context.Context, stream pb.TunnelService_OpenTunnelClient, writeMu *sync.Mutex) error {
	for {
		msg, err := stream.Recv()
		if err != nil {
			if err == io.EOF || ctx.Err() != nil {
				return nil
			}
			return fmt.Errorf("recv: %w", err)
		}

		switch p := msg.Payload.(type) {
		case *pb.TunnelServerMessage_HttpRequest:
			go func(frame *pb.HttpRequestFrame) {
				resp := m.proxy.Handle(ctx, frame)
				writeMu.Lock()
				defer writeMu.Unlock()
				_ = stream.Send(&pb.TunnelClientMessage{
					Payload: &pb.TunnelClientMessage_HttpResponse{HttpResponse: resp},
				})
			}(p.HttpRequest)

		case *pb.TunnelServerMessage_Ping:
			writeMu.Lock()
			_ = stream.Send(&pb.TunnelClientMessage{
				Payload: &pb.TunnelClientMessage_Pong{
					Pong: &pb.PongFrame{
						Payload:        p.Ping.Payload,
						OriginalSentAt: p.Ping.SentAt,
					},
				},
			})
			writeMu.Unlock()

		case *pb.TunnelServerMessage_Pong:
			// RTT measurement — no-op.

		case *pb.TunnelServerMessage_Cancel:
			m.logger.Debug("Cancel requested", "requestId", p.Cancel.RequestId)

		default:
			m.logger.Warn("Unknown server message type", "payload", fmt.Sprintf("%T", msg.Payload))
		}
	}
}

func (m *Manager) heartbeat(ctx context.Context, client pb.ClusterServiceClient) error {
	ticker := time.NewTicker(m.cfg.HeartbeatInterval)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return nil
		case <-ticker.C:
			_, err := client.UpdateStatus(ctx, &pb.UpdateClusterStatusRequest{
				ClusterId:         m.cfg.ClusterID,
				Status:            pb.ClusterStatus_CLUSTER_STATUS_CONNECTED,
				KubernetesVersion: "",
			})
			if err != nil {
				m.logger.Warn("Heartbeat failed", "error", err)
			}
		}
	}
}
