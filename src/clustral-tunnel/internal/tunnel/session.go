package tunnel

import (
	"context"
	"fmt"
	"sync"

	pb "clustral-tunnel/gen/clustral/v1"

	"google.golang.org/grpc"
)

// Session represents a single active tunnel connection from an Agent.
type Session struct {
	ClusterID string
	writer    grpc.ServerStream
	writeMu   sync.Mutex
	pending   sync.Map // map[string]chan *pb.HttpResponseFrame
}

// NewSession creates a new tunnel session.
func NewSession(clusterID string, writer grpc.ServerStream) *Session {
	return &Session{
		ClusterID: clusterID,
		writer:    writer,
	}
}

// ProxyAsync sends an HTTP request frame to the agent and waits for the response.
func (s *Session) ProxyAsync(ctx context.Context, frame *pb.HttpRequestFrame) (*pb.HttpResponseFrame, error) {
	ch := make(chan *pb.HttpResponseFrame, 1)
	s.pending.Store(frame.RequestId, ch)
	defer s.pending.Delete(frame.RequestId)

	// Send the request frame to the agent.
	s.writeMu.Lock()
	err := s.writer.SendMsg(&pb.TunnelServerMessage{
		Payload: &pb.TunnelServerMessage_HttpRequest{
			HttpRequest: frame,
		},
	})
	s.writeMu.Unlock()

	if err != nil {
		return nil, fmt.Errorf("send request frame: %w", err)
	}

	// Wait for the response or context cancellation.
	select {
	case resp := <-ch:
		return resp, nil
	case <-ctx.Done():
		return nil, ctx.Err()
	}
}

// HandleHttpResponse delivers a response frame to the waiting ProxyAsync caller.
func (s *Session) HandleHttpResponse(frame *pb.HttpResponseFrame) {
	if ch, ok := s.pending.Load(frame.RequestId); ok {
		ch.(chan *pb.HttpResponseFrame) <- frame
	}
}

// SendPong sends a pong frame to the agent.
func (s *Session) SendPong(ping *pb.PingFrame) error {
	s.writeMu.Lock()
	defer s.writeMu.Unlock()
	return s.writer.SendMsg(&pb.TunnelServerMessage{
		Payload: &pb.TunnelServerMessage_Pong{
			Pong: &pb.PongFrame{
				Payload:        ping.Payload,
				OriginalSentAt: ping.SentAt,
			},
		},
	})
}

// SendHello sends a tunnel hello to the agent.
func (s *Session) SendHello(hello *pb.TunnelHello) error {
	s.writeMu.Lock()
	defer s.writeMu.Unlock()
	return s.writer.SendMsg(&pb.TunnelServerMessage{
		Payload: &pb.TunnelServerMessage_Hello{
			Hello: hello,
		},
	})
}
