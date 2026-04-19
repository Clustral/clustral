package cluster

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"crypto/x509"
	"encoding/base64"
	"encoding/hex"
	"encoding/pem"
	"fmt"
	"log/slog"
	"math"
	"time"

	pb "clustral-tunnel/gen/clustral/v1"
	"clustral-tunnel/internal/tunnel"

	"go.mongodb.org/mongo-driver/v2/bson"
	"go.mongodb.org/mongo-driver/v2/mongo"
	"go.mongodb.org/mongo-driver/v2/mongo/options"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/status"
	"google.golang.org/protobuf/types/known/emptypb"
	"google.golang.org/protobuf/types/known/timestamppb"
)

// DefaultAllowedRPCs is the default set of RPCs an agent is permitted to call.
var DefaultAllowedRPCs = []string{
	"ClusterService/RegisterAgent",
	"ClusterService/UpdateStatus",
	"ClusterService/RenewCertificate",
	"ClusterService/RenewToken",
	"TunnelService/OpenTunnel",
}

// ClusterServiceServer implements the ClusterService gRPC server.
type ClusterServiceServer struct {
	pb.UnimplementedClusterServiceServer
	mongoDB   *mongo.Database
	ca        *CA
	jwtSigner *JWTSigner
	manager   *tunnel.SessionManager
	logger    *slog.Logger
}

// NewClusterServiceServer creates a new ClusterServiceServer.
func NewClusterServiceServer(
	db *mongo.Database,
	ca *CA,
	jwtSigner *JWTSigner,
	manager *tunnel.SessionManager,
	logger *slog.Logger,
) *ClusterServiceServer {
	return &ClusterServiceServer{
		mongoDB:   db,
		ca:        ca,
		jwtSigner: jwtSigner,
		manager:   manager,
		logger:    logger,
	}
}

// clusterDoc mirrors the MongoDB cluster document.
type clusterDoc struct {
	ID                     string            `bson:"_id"`
	Name                   string            `bson:"Name"`
	Description            string            `bson:"Description"`
	BootstrapTokenHash     *string           `bson:"BootstrapTokenHash,omitempty"`
	KubernetesVersion      *string           `bson:"KubernetesVersion,omitempty"`
	AgentVersion           *string           `bson:"AgentVersion,omitempty"`
	Status                 string            `bson:"Status"`
	RegisteredAt           time.Time         `bson:"RegisteredAt"`
	LastSeenAt             *time.Time        `bson:"LastSeenAt,omitempty"`
	Labels                 map[string]string `bson:"Labels"`
	TokenVersion           int               `bson:"TokenVersion"`
	CertificateFingerprint *string           `bson:"CertificateFingerprint,omitempty"`
}

func (s *ClusterServiceServer) Register(ctx context.Context, req *pb.RegisterClusterRequest) (*pb.RegisterClusterResponse, error) {
	if req.Name == "" {
		return nil, status.Error(codes.InvalidArgument, "name is required")
	}

	// Check for duplicate names.
	clusters := s.mongoDB.Collection("clusters")
	count, err := clusters.CountDocuments(ctx, bson.M{"Name": req.Name})
	if err != nil {
		return nil, status.Errorf(codes.Internal, "check duplicate: %v", err)
	}
	if count > 0 {
		return nil, status.Errorf(codes.AlreadyExists, "a cluster named '%s' is already registered", req.Name)
	}

	// Generate bootstrap token.
	token := generateToken()
	tokenHash := hashToken(token)

	id := generateID()
	doc := clusterDoc{
		ID:                 id,
		Name:               req.Name,
		Description:        req.Description,
		BootstrapTokenHash: &tokenHash,
		Status:             "Pending",
		RegisteredAt:       time.Now().UTC(),
		Labels:             req.Labels,
		TokenVersion:       1,
	}

	if doc.Labels == nil {
		doc.Labels = make(map[string]string)
	}

	_, err = clusters.InsertOne(ctx, doc)
	if err != nil {
		return nil, status.Errorf(codes.Internal, "insert cluster: %v", err)
	}

	s.logger.Info("Cluster registered", "name", req.Name, "id", id)

	return &pb.RegisterClusterResponse{
		ClusterId:      id,
		BootstrapToken: token,
	}, nil
}

func (s *ClusterServiceServer) RegisterAgent(ctx context.Context, req *pb.RegisterAgentRequest) (*pb.RegisterAgentResponse, error) {
	if req.ClusterId == "" {
		return nil, status.Error(codes.InvalidArgument, "cluster_id is required")
	}
	if req.BootstrapToken == "" {
		return nil, status.Error(codes.InvalidArgument, "bootstrap_token is required")
	}

	clusters := s.mongoDB.Collection("clusters")
	var doc clusterDoc
	err := clusters.FindOne(ctx, bson.M{"_id": req.ClusterId}).Decode(&doc)
	if err != nil {
		if err == mongo.ErrNoDocuments {
			return nil, status.Errorf(codes.NotFound, "cluster %s not found", req.ClusterId)
		}
		return nil, status.Errorf(codes.Internal, "find cluster: %v", err)
	}

	// Verify bootstrap token.
	if doc.BootstrapTokenHash == nil || *doc.BootstrapTokenHash == "" {
		return nil, status.Error(codes.FailedPrecondition,
			"bootstrap token has already been consumed; re-register the cluster to generate a new one")
	}

	incomingHash := hashToken(req.BootstrapToken)
	if incomingHash != *doc.BootstrapTokenHash {
		return nil, status.Error(codes.Unauthenticated, "invalid bootstrap token")
	}

	// Issue client certificate.
	agentID := req.ClusterId
	certPEM, keyPEM, err := s.ca.IssueCertificate(agentID, 365)
	if err != nil {
		return nil, status.Errorf(codes.Internal, "issue certificate: %v", err)
	}

	// Issue JWT.
	jwt, err := s.jwtSigner.IssueToken(agentID, "clustral", agentID, DefaultAllowedRPCs, doc.TokenVersion)
	if err != nil {
		return nil, status.Errorf(codes.Internal, "issue JWT: %v", err)
	}
	jwtExpiry := s.jwtSigner.GetTokenExpiry()

	// Parse cert to get expiry and fingerprint.
	block, _ := pem.Decode(certPEM)
	clientCert, _ := x509.ParseCertificate(block.Bytes)
	fingerprint := certFingerprint(clientCert)

	// Consume bootstrap token and record fingerprint.
	_, err = clusters.UpdateOne(ctx,
		bson.M{"_id": req.ClusterId},
		bson.M{"$set": bson.M{
			"BootstrapTokenHash":     nil,
			"CertificateFingerprint": fingerprint,
		}},
	)
	if err != nil {
		s.logger.Warn("Failed to consume bootstrap token", "clusterId", req.ClusterId, "error", err)
	}

	s.logger.Info("Agent registered with mTLS + JWT", "clusterId", req.ClusterId)

	return &pb.RegisterAgentResponse{
		ClusterId:            req.ClusterId,
		ClientCertificatePem: string(certPEM),
		ClientPrivateKeyPem:  string(keyPEM),
		CaCertificatePem:     s.ca.GetCACertificatePEM(),
		Jwt:                  jwt,
		CertExpiresAt:        timestamppb.New(clientCert.NotAfter),
		JwtExpiresAt:         timestamppb.New(jwtExpiry),
	}, nil
}

func (s *ClusterServiceServer) RenewCertificate(ctx context.Context, req *pb.RenewCertificateRequest) (*pb.RenewCertificateResponse, error) {
	if req.ClusterId == "" {
		return nil, status.Error(codes.InvalidArgument, "cluster_id is required")
	}

	clusters := s.mongoDB.Collection("clusters")
	count, err := clusters.CountDocuments(ctx, bson.M{"_id": req.ClusterId})
	if err != nil {
		return nil, status.Errorf(codes.Internal, "find cluster: %v", err)
	}
	if count == 0 {
		return nil, status.Errorf(codes.NotFound, "cluster %s not found", req.ClusterId)
	}

	certPEM, keyPEM, err := s.ca.IssueCertificate(req.ClusterId, 365)
	if err != nil {
		return nil, status.Errorf(codes.Internal, "issue certificate: %v", err)
	}

	block, _ := pem.Decode(certPEM)
	clientCert, _ := x509.ParseCertificate(block.Bytes)
	fingerprint := certFingerprint(clientCert)

	_, _ = clusters.UpdateOne(ctx,
		bson.M{"_id": req.ClusterId},
		bson.M{"$set": bson.M{"CertificateFingerprint": fingerprint}},
	)

	s.logger.Info("Certificate renewed", "clusterId", req.ClusterId)

	return &pb.RenewCertificateResponse{
		ClientCertificatePem: string(certPEM),
		ClientPrivateKeyPem:  string(keyPEM),
		ExpiresAt:            timestamppb.New(clientCert.NotAfter),
	}, nil
}

func (s *ClusterServiceServer) RenewToken(ctx context.Context, req *pb.RenewTokenRequest) (*pb.RenewTokenResponse, error) {
	if req.ClusterId == "" {
		return nil, status.Error(codes.InvalidArgument, "cluster_id is required")
	}

	clusters := s.mongoDB.Collection("clusters")
	var doc clusterDoc
	err := clusters.FindOne(ctx, bson.M{"_id": req.ClusterId}).Decode(&doc)
	if err != nil {
		if err == mongo.ErrNoDocuments {
			return nil, status.Errorf(codes.NotFound, "cluster %s not found", req.ClusterId)
		}
		return nil, status.Errorf(codes.Internal, "find cluster: %v", err)
	}

	jwt, err := s.jwtSigner.IssueToken(req.ClusterId, "clustral", req.ClusterId, DefaultAllowedRPCs, doc.TokenVersion)
	if err != nil {
		return nil, status.Errorf(codes.Internal, "issue JWT: %v", err)
	}

	s.logger.Info("JWT renewed", "clusterId", req.ClusterId, "tokenVersion", doc.TokenVersion)

	return &pb.RenewTokenResponse{
		Jwt:       jwt,
		ExpiresAt: timestamppb.New(s.jwtSigner.GetTokenExpiry()),
	}, nil
}

func (s *ClusterServiceServer) UpdateStatus(ctx context.Context, req *pb.UpdateClusterStatusRequest) (*emptypb.Empty, error) {
	if req.ClusterId == "" {
		return nil, status.Error(codes.InvalidArgument, "cluster_id is required")
	}

	clusters := s.mongoDB.Collection("clusters")
	result, err := clusters.UpdateOne(ctx,
		bson.M{"_id": req.ClusterId},
		bson.M{"$set": bson.M{
			"Status":     mapStatusToString(req.Status),
			"LastSeenAt": time.Now().UTC(),
		}},
	)
	if err != nil {
		return nil, status.Errorf(codes.Internal, "update status: %v", err)
	}
	if result.MatchedCount == 0 {
		return nil, status.Errorf(codes.NotFound, "cluster %s not found", req.ClusterId)
	}

	// Refresh Redis TTL.
	_ = s.manager.Refresh(ctx, req.ClusterId)

	return &emptypb.Empty{}, nil
}

func (s *ClusterServiceServer) List(ctx context.Context, req *pb.ListClustersRequest) (*pb.ListClustersResponse, error) {
	filter := bson.M{}

	if req.StatusFilter != pb.ClusterStatus_CLUSTER_STATUS_UNSPECIFIED {
		filter["Status"] = mapStatusToString(req.StatusFilter)
	}

	for k, v := range req.LabelSelector {
		filter[fmt.Sprintf("Labels.%s", k)] = v
	}

	pageSize := int64(50)
	if req.PageSize > 0 {
		pageSize = int64(math.Min(float64(req.PageSize), 200))
	}

	clusters := s.mongoDB.Collection("clusters")
	opts := options.Find().SetSort(bson.M{"_id": 1}).SetLimit(pageSize)
	cursor, err := clusters.Find(ctx, filter, opts)
	if err != nil {
		return nil, status.Errorf(codes.Internal, "list clusters: %v", err)
	}
	defer cursor.Close(ctx)

	var docs []clusterDoc
	if err := cursor.All(ctx, &docs); err != nil {
		return nil, status.Errorf(codes.Internal, "decode clusters: %v", err)
	}

	resp := &pb.ListClustersResponse{}
	for _, doc := range docs {
		resp.Clusters = append(resp.Clusters, docToProto(&doc))
	}
	return resp, nil
}

func (s *ClusterServiceServer) Get(ctx context.Context, req *pb.GetClusterRequest) (*pb.Cluster, error) {
	if req.ClusterId == "" {
		return nil, status.Error(codes.InvalidArgument, "cluster_id is required")
	}

	clusters := s.mongoDB.Collection("clusters")
	var doc clusterDoc
	err := clusters.FindOne(ctx, bson.M{"_id": req.ClusterId}).Decode(&doc)
	if err != nil {
		if err == mongo.ErrNoDocuments {
			return nil, status.Errorf(codes.NotFound, "cluster %s not found", req.ClusterId)
		}
		return nil, status.Errorf(codes.Internal, "find cluster: %v", err)
	}

	return docToProto(&doc), nil
}

func (s *ClusterServiceServer) Deregister(ctx context.Context, req *pb.DeregisterClusterRequest) (*emptypb.Empty, error) {
	if req.ClusterId == "" {
		return nil, status.Error(codes.InvalidArgument, "cluster_id is required")
	}

	clusters := s.mongoDB.Collection("clusters")
	result, err := clusters.DeleteOne(ctx, bson.M{"_id": req.ClusterId})
	if err != nil {
		return nil, status.Errorf(codes.Internal, "delete cluster: %v", err)
	}
	if result.DeletedCount == 0 {
		return nil, status.Errorf(codes.NotFound, "cluster %s not found", req.ClusterId)
	}

	// Cascade: delete access tokens.
	tokens := s.mongoDB.Collection("access_tokens")
	_, _ = tokens.DeleteMany(ctx, bson.M{"ClusterId": req.ClusterId})

	s.logger.Info("Cluster deregistered", "clusterId", req.ClusterId)
	return &emptypb.Empty{}, nil
}

// GetTokenVersion returns the token version for a cluster (used by auth interceptor).
func (s *ClusterServiceServer) GetTokenVersion(ctx context.Context, clusterID string) (int, error) {
	clusters := s.mongoDB.Collection("clusters")
	var doc clusterDoc
	err := clusters.FindOne(ctx, bson.M{"_id": clusterID}).Decode(&doc)
	if err != nil {
		if err == mongo.ErrNoDocuments {
			return 0, nil
		}
		return 0, err
	}
	return doc.TokenVersion, nil
}

// Helpers

func docToProto(doc *clusterDoc) *pb.Cluster {
	c := &pb.Cluster{
		Id:           doc.ID,
		Name:         doc.Name,
		Description:  doc.Description,
		Status:       mapStringToStatus(doc.Status),
		RegisteredAt: timestamppb.New(doc.RegisteredAt),
		Labels:       doc.Labels,
	}
	if doc.KubernetesVersion != nil {
		c.KubernetesVersion = *doc.KubernetesVersion
	}
	if doc.LastSeenAt != nil {
		c.LastSeenAt = timestamppb.New(*doc.LastSeenAt)
	}
	return c
}

func mapStatusToString(s pb.ClusterStatus) string {
	switch s {
	case pb.ClusterStatus_CLUSTER_STATUS_PENDING:
		return "Pending"
	case pb.ClusterStatus_CLUSTER_STATUS_CONNECTED:
		return "Connected"
	case pb.ClusterStatus_CLUSTER_STATUS_DISCONNECTED:
		return "Disconnected"
	default:
		return "Pending"
	}
}

func mapStringToStatus(s string) pb.ClusterStatus {
	switch s {
	case "Pending":
		return pb.ClusterStatus_CLUSTER_STATUS_PENDING
	case "Connected":
		return pb.ClusterStatus_CLUSTER_STATUS_CONNECTED
	case "Disconnected":
		return pb.ClusterStatus_CLUSTER_STATUS_DISCONNECTED
	default:
		return pb.ClusterStatus_CLUSTER_STATUS_UNSPECIFIED
	}
}

func generateToken() string {
	b := make([]byte, 32)
	_, _ = rand.Read(b)
	return base64.RawURLEncoding.EncodeToString(b)
}

func generateID() string {
	b := make([]byte, 16)
	_, _ = rand.Read(b)
	// Format as UUID-like string.
	return fmt.Sprintf("%08x-%04x-%04x-%04x-%012x",
		b[0:4], b[4:6], b[6:8], b[8:10], b[10:16])
}

func hashToken(raw string) string {
	h := sha256.Sum256([]byte(raw))
	return hex.EncodeToString(h[:])
}

func certFingerprint(cert *x509.Certificate) string {
	h := sha256.Sum256(cert.Raw)
	return hex.EncodeToString(h[:])
}
