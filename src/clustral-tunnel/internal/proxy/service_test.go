package proxy

import (
	"context"
	"sync"
	"testing"
	"time"

	pb "clustral-tunnel/gen/clustral/v1"
	internalpb "clustral-tunnel/gen/tunnelproxy/v1"
	tunnelredis "clustral-tunnel/internal/redis"
	"clustral-tunnel/internal/tunnel"

	"github.com/alicebob/miniredis/v2"
	goredis "github.com/redis/go-redis/v9"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/status"
)

// mockStream implements grpc.ServerStream for testing.
type mockStream struct {
	grpc.ServerStream
	mu       sync.Mutex
	messages []*pb.TunnelServerMessage
}

func (m *mockStream) SendMsg(msg interface{}) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.messages = append(m.messages, msg.(*pb.TunnelServerMessage))
	return nil
}

func (m *mockStream) Context() context.Context {
	return context.Background()
}

func (m *mockStream) SetHeader(metadata.MD) error  { return nil }
func (m *mockStream) SendHeader(metadata.MD) error { return nil }
func (m *mockStream) SetTrailer(metadata.MD)        {}

func setupProxy(t *testing.T) (*TunnelProxyServer, *tunnel.SessionManager) {
	t.Helper()
	mr := miniredis.RunT(t)
	client := goredis.NewClient(&goredis.Options{Addr: mr.Addr()})
	registry := tunnelredis.NewSessionRegistryFromClient(client, 5*time.Minute)
	mgr := tunnel.NewSessionManager(registry, "tunnel-0")
	srv := NewTunnelProxyServer(mgr)
	return srv, mgr
}

func TestProxyRequest_SessionExists(t *testing.T) {
	srv, mgr := setupProxy(t)
	ctx := context.Background()

	stream := &mockStream{}
	session := tunnel.NewSession("cluster-1", stream)
	_ = mgr.Register(ctx, "cluster-1", session)

	// Simulate response arriving.
	go func() {
		time.Sleep(10 * time.Millisecond)
		session.HandleHttpResponse(&pb.HttpResponseFrame{
			RequestId: "req-1",
			Head:      &pb.HttpResponseHead{StatusCode: 200},
			EndOfBody: true,
		})
	}()

	reqCtx, cancel := context.WithTimeout(ctx, 2*time.Second)
	defer cancel()

	resp, err := srv.ProxyRequest(reqCtx, &internalpb.TunnelProxyRequest{
		ClusterId: "cluster-1",
		Frame: &pb.HttpRequestFrame{
			RequestId: "req-1",
			EndOfBody: true,
		},
	})

	require.NoError(t, err)
	assert.Equal(t, int32(200), resp.Frame.Head.StatusCode)
}

func TestProxyRequest_NoSession(t *testing.T) {
	srv, _ := setupProxy(t)

	_, err := srv.ProxyRequest(context.Background(), &internalpb.TunnelProxyRequest{
		ClusterId: "nonexistent",
		Frame: &pb.HttpRequestFrame{
			RequestId: "req-1",
			EndOfBody: true,
		},
	})

	st, _ := status.FromError(err)
	assert.Equal(t, codes.NotFound, st.Code())
}
