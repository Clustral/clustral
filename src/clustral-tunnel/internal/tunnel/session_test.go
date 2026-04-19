package tunnel

import (
	"context"
	"sync"
	"testing"
	"time"

	pb "clustral-tunnel/gen/clustral/v1"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"google.golang.org/grpc"
	"google.golang.org/grpc/metadata"
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

func TestSession_ProxyAsync(t *testing.T) {
	stream := &mockStream{}
	session := NewSession("cluster-1", stream)

	frame := &pb.HttpRequestFrame{
		RequestId: "req-1",
		Head: &pb.HttpRequestHead{
			Method: "GET",
			Path:   "/api/v1/pods",
		},
		EndOfBody: true,
	}

	// Simulate response arriving concurrently.
	go func() {
		time.Sleep(10 * time.Millisecond)
		session.HandleHttpResponse(&pb.HttpResponseFrame{
			RequestId: "req-1",
			Head: &pb.HttpResponseHead{
				StatusCode: 200,
			},
			EndOfBody: true,
		})
	}()

	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()

	resp, err := session.ProxyAsync(ctx, frame)
	require.NoError(t, err)
	assert.Equal(t, "req-1", resp.RequestId)
	assert.Equal(t, int32(200), resp.Head.StatusCode)

	// Verify the request was sent to the stream.
	stream.mu.Lock()
	assert.Len(t, stream.messages, 1)
	assert.NotNil(t, stream.messages[0].GetHttpRequest())
	stream.mu.Unlock()
}

func TestSession_ProxyAsync_Timeout(t *testing.T) {
	stream := &mockStream{}
	session := NewSession("cluster-1", stream)

	frame := &pb.HttpRequestFrame{
		RequestId: "req-timeout",
		EndOfBody: true,
	}

	ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()

	_, err := session.ProxyAsync(ctx, frame)
	assert.Error(t, err)
	assert.ErrorIs(t, err, context.DeadlineExceeded)
}

func TestSession_ConcurrentProxy(t *testing.T) {
	stream := &mockStream{}
	session := NewSession("cluster-1", stream)

	var wg sync.WaitGroup
	for i := 0; i < 5; i++ {
		wg.Add(1)
		reqID := "req-" + string(rune('A'+i))
		go func(id string) {
			defer wg.Done()

			frame := &pb.HttpRequestFrame{
				RequestId: id,
				EndOfBody: true,
			}

			// Simulate response.
			go func() {
				time.Sleep(10 * time.Millisecond)
				session.HandleHttpResponse(&pb.HttpResponseFrame{
					RequestId: id,
					Head: &pb.HttpResponseHead{
						StatusCode: 200,
					},
					EndOfBody: true,
				})
			}()

			ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
			defer cancel()

			resp, err := session.ProxyAsync(ctx, frame)
			require.NoError(t, err)
			assert.Equal(t, id, resp.RequestId)
		}(reqID)
	}

	wg.Wait()
}
