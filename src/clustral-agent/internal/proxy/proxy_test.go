package proxy

import (
	"context"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	pb "clustral-agent/gen/clustral/v1"
)

func testLogger() *slog.Logger {
	return slog.New(slog.NewTextHandler(io.Discard, nil))
}

func TestHandle_HappyPath_GET(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != "GET" {
			t.Errorf("method = %s, want GET", r.Method)
		}
		if r.URL.Path != "/api/v1/pods" {
			t.Errorf("path = %s, want /api/v1/pods", r.URL.Path)
		}
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(200)
		w.Write([]byte(`{"items":[]}`))
	}))
	defer server.Close()

	p := &Proxy{apiURL: server.URL, httpClient: server.Client(), logger: testLogger()}
	frame := &pb.HttpRequestFrame{
		RequestId: "req-1",
		Head:      &pb.HttpRequestHead{Method: "GET", Path: "/api/v1/pods"},
	}

	resp := p.Handle(context.Background(), frame)

	if resp.Error != nil {
		t.Fatalf("unexpected error: %v", resp.Error)
	}
	if resp.Head.StatusCode != 200 {
		t.Errorf("status = %d, want 200", resp.Head.StatusCode)
	}
	if resp.RequestId != "req-1" {
		t.Errorf("requestId = %q, want req-1", resp.RequestId)
	}
	if !resp.EndOfBody {
		t.Error("EndOfBody should be true")
	}
	if !strings.Contains(string(resp.BodyChunk), "items") {
		t.Error("body should contain items")
	}
}

func TestHandle_POST_WithBody(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := io.ReadAll(r.Body)
		if string(body) != `{"name":"test"}` {
			t.Errorf("body = %q", string(body))
		}
		w.WriteHeader(201)
	}))
	defer server.Close()

	p := &Proxy{apiURL: server.URL, httpClient: server.Client(), logger: testLogger()}
	frame := &pb.HttpRequestFrame{
		RequestId: "req-2",
		Head:      &pb.HttpRequestHead{Method: "POST", Path: "/api/v1/pods"},
		BodyChunk: []byte(`{"name":"test"}`),
	}

	resp := p.Handle(context.Background(), frame)

	if resp.Error != nil {
		t.Fatalf("unexpected error: %v", resp.Error)
	}
	if resp.Head.StatusCode != 201 {
		t.Errorf("status = %d, want 201", resp.Head.StatusCode)
	}
}

func TestHandle_NoHead_ReturnsError(t *testing.T) {
	p := &Proxy{apiURL: "http://unused", httpClient: http.DefaultClient, logger: testLogger()}
	frame := &pb.HttpRequestFrame{RequestId: "req-err", Head: nil}

	resp := p.Handle(context.Background(), frame)

	if resp.Error == nil {
		t.Fatal("expected error frame for nil head")
	}
	if resp.Error.Code != pb.TunnelErrorCode_TUNNEL_ERROR_UNSPECIFIED {
		t.Errorf("error code = %v", resp.Error.Code)
	}
	if resp.RequestId != "req-err" {
		t.Errorf("requestId = %q", resp.RequestId)
	}
}

func TestHandle_ImpersonateUser(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if got := r.Header.Get("Impersonate-User"); got != "alice@example.com" {
			t.Errorf("Impersonate-User = %q, want alice@example.com", got)
		}
		w.WriteHeader(200)
	}))
	defer server.Close()

	p := &Proxy{apiURL: server.URL, httpClient: server.Client(), logger: testLogger()}
	frame := &pb.HttpRequestFrame{
		RequestId: "req-imp",
		Head: &pb.HttpRequestHead{
			Method: "GET", Path: "/",
			Headers: []*pb.HttpHeader{
				{Name: "X-Clustral-Impersonate-User", Value: "alice@example.com"},
			},
		},
	}

	resp := p.Handle(context.Background(), frame)
	if resp.Error != nil {
		t.Fatalf("unexpected error: %v", resp.Error)
	}
}

func TestHandle_ImpersonateGroup_MultipleValues(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		groups := r.Header.Values("Impersonate-Group")
		if len(groups) != 2 {
			t.Errorf("Impersonate-Group count = %d, want 2; values = %v", len(groups), groups)
		}
		w.WriteHeader(200)
	}))
	defer server.Close()

	p := &Proxy{apiURL: server.URL, httpClient: server.Client(), logger: testLogger()}
	frame := &pb.HttpRequestFrame{
		RequestId: "req-grp",
		Head: &pb.HttpRequestHead{
			Method: "GET", Path: "/",
			Headers: []*pb.HttpHeader{
				{Name: "X-Clustral-Impersonate-Group", Value: "system:masters"},
				{Name: "X-Clustral-Impersonate-Group", Value: "system:authenticated"},
			},
		},
	}

	resp := p.Handle(context.Background(), frame)
	if resp.Error != nil {
		t.Fatalf("unexpected error: %v", resp.Error)
	}
}

func TestHandle_HopByHopHeaders_Stripped(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("Authorization") != "" {
			t.Error("Authorization header should be stripped")
		}
		if r.Header.Get("Connection") != "" {
			t.Error("Connection header should be stripped")
		}
		if r.Header.Get("Accept") != "application/json" {
			t.Errorf("Accept header should be preserved, got %q", r.Header.Get("Accept"))
		}
		w.WriteHeader(200)
	}))
	defer server.Close()

	p := &Proxy{apiURL: server.URL, httpClient: server.Client(), logger: testLogger()}
	frame := &pb.HttpRequestFrame{
		RequestId: "req-hop",
		Head: &pb.HttpRequestHead{
			Method: "GET", Path: "/",
			Headers: []*pb.HttpHeader{
				{Name: "Authorization", Value: "Bearer secret"},
				{Name: "Connection", Value: "keep-alive"},
				{Name: "Accept", Value: "application/json"},
			},
		},
	}

	resp := p.Handle(context.Background(), frame)
	if resp.Error != nil {
		t.Fatalf("unexpected error: %v", resp.Error)
	}
}

func TestHandle_ClustralHeaders_Stripped(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("X-Clustral-Custom") != "" {
			t.Error("X-Clustral-Custom should be stripped")
		}
		w.WriteHeader(200)
	}))
	defer server.Close()

	p := &Proxy{apiURL: server.URL, httpClient: server.Client(), logger: testLogger()}
	frame := &pb.HttpRequestFrame{
		RequestId: "req-custom",
		Head: &pb.HttpRequestHead{
			Method: "GET", Path: "/",
			Headers: []*pb.HttpHeader{
				{Name: "X-Clustral-Custom", Value: "should-not-forward"},
			},
		},
	}

	resp := p.Handle(context.Background(), frame)
	if resp.Error != nil {
		t.Fatalf("unexpected error: %v", resp.Error)
	}
}

func TestHandle_APIUnreachable(t *testing.T) {
	p := &Proxy{apiURL: "http://127.0.0.1:1", httpClient: &http.Client{}, logger: testLogger()}
	frame := &pb.HttpRequestFrame{
		RequestId: "req-unreach",
		Head:      &pb.HttpRequestHead{Method: "GET", Path: "/"},
	}

	resp := p.Handle(context.Background(), frame)

	if resp.Error == nil {
		t.Fatal("expected error for unreachable API")
	}
	if resp.Error.Code != pb.TunnelErrorCode_TUNNEL_ERROR_API_SERVER_UNREACHABLE {
		t.Errorf("error code = %v, want API_SERVER_UNREACHABLE", resp.Error.Code)
	}
}

func TestHandle_ResponseHeaders_Copied(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-Custom", "test-value")
		w.WriteHeader(200)
	}))
	defer server.Close()

	p := &Proxy{apiURL: server.URL, httpClient: server.Client(), logger: testLogger()}
	frame := &pb.HttpRequestFrame{
		RequestId: "req-hdrs",
		Head:      &pb.HttpRequestHead{Method: "GET", Path: "/"},
	}

	resp := p.Handle(context.Background(), frame)
	if resp.Error != nil {
		t.Fatalf("unexpected error: %v", resp.Error)
	}

	found := false
	for _, h := range resp.Head.Headers {
		if h.Name == "X-Custom" && h.Value == "test-value" {
			found = true
		}
	}
	if !found {
		t.Error("response should include X-Custom header")
	}
}

func TestErrorFrame(t *testing.T) {
	f := errorFrame("req-123", pb.TunnelErrorCode_TUNNEL_ERROR_UNSPECIFIED, "something broke")

	if f.RequestId != "req-123" {
		t.Errorf("requestId = %q", f.RequestId)
	}
	if !f.EndOfBody {
		t.Error("EndOfBody should be true")
	}
	if f.Error.Code != pb.TunnelErrorCode_TUNNEL_ERROR_UNSPECIFIED {
		t.Errorf("error code = %v", f.Error.Code)
	}
	if f.Error.Message != "something broke" {
		t.Errorf("message = %q", f.Error.Message)
	}
}

func TestSaTokenRoundTripper(t *testing.T) {
	dir := t.TempDir()
	tokenFile := filepath.Join(dir, "token")
	os.WriteFile(tokenFile, []byte("  sa-jwt-token  \n"), 0600)

	inner := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		auth := r.Header.Get("Authorization")
		if auth != "Bearer sa-jwt-token" {
			t.Errorf("Authorization = %q, want 'Bearer sa-jwt-token'", auth)
		}
		w.WriteHeader(200)
	})
	server := httptest.NewServer(inner)
	defer server.Close()

	rt := &saTokenRoundTripper{
		inner:     server.Client().Transport,
		tokenPath: tokenFile,
	}

	req, _ := http.NewRequest("GET", server.URL, nil)
	resp, err := rt.RoundTrip(req)
	if err != nil {
		t.Fatalf("RoundTrip error: %v", err)
	}
	if resp.StatusCode != 200 {
		t.Errorf("status = %d", resp.StatusCode)
	}
}

func TestSaTokenRoundTripper_FileMissing(t *testing.T) {
	rt := &saTokenRoundTripper{
		inner:     http.DefaultTransport,
		tokenPath: "/nonexistent/sa/token",
	}

	req, _ := http.NewRequest("GET", "http://localhost", nil)
	_, err := rt.RoundTrip(req)
	if err == nil {
		t.Error("expected error when SA token file is missing")
	}
}
