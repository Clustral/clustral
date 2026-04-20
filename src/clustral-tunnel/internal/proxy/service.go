package proxy

import (
	"context"

	internalpb "clustral-tunnel/gen/tunnelproxy/v1"
	"clustral-tunnel/internal/tunnel"

	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/status"
)

// TunnelProxyServer implements the internal TunnelProxy gRPC service.
type TunnelProxyServer struct {
	internalpb.UnimplementedTunnelProxyServer
	manager *tunnel.SessionManager
}

// NewTunnelProxyServer creates a new TunnelProxyServer.
func NewTunnelProxyServer(manager *tunnel.SessionManager) *TunnelProxyServer {
	return &TunnelProxyServer{manager: manager}
}

// ProxyRequest forwards an HTTP request through an agent tunnel session.
func (s *TunnelProxyServer) ProxyRequest(ctx context.Context, req *internalpb.TunnelProxyRequest) (*internalpb.TunnelProxyResponse, error) {
	session, ok := s.manager.GetSession(req.ClusterId)
	if !ok {
		return nil, status.Errorf(codes.NotFound, "no active tunnel session for cluster %s", req.ClusterId)
	}

	resp, err := session.ProxyAsync(ctx, req.Frame)
	if err != nil {
		return nil, status.Errorf(codes.Internal, "proxy request failed: %v", err)
	}

	return &internalpb.TunnelProxyResponse{
		Frame: resp,
	}, nil
}
