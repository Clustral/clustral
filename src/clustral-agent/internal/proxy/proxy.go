package proxy

import (
	"bytes"
	"context"
	"crypto/tls"
	"crypto/x509"
	"io"
	"log/slog"
	"net/http"
	"os"
	"strings"

	pb "github.com/Clustral/clustral/src/clustral-agent/gen/clustralv1"
)

var hopByHopHeaders = map[string]bool{
	"connection":          true,
	"keep-alive":          true,
	"proxy-authenticate":  true,
	"proxy-authorization": true,
	"te":                  true,
	"trailers":            true,
	"transfer-encoding":   true,
	"upgrade":             true,
	"authorization":       true, // Strip user's Clustral token.
}

type Proxy struct {
	apiURL     string
	httpClient *http.Client
	logger     *slog.Logger
}

func New(apiURL string, skipTLSVerify bool, logger *slog.Logger) *Proxy {
	client := buildHTTPClient(apiURL, skipTLSVerify)
	return &Proxy{
		apiURL:     strings.TrimRight(apiURL, "/"),
		httpClient: client,
		logger:     logger,
	}
}

func (p *Proxy) Handle(ctx context.Context, frame *pb.HttpRequestFrame) *pb.HttpResponseFrame {
	if frame.Head == nil {
		return errorFrame(frame.RequestId, pb.TunnelErrorCode_TUNNEL_ERROR_UNSPECIFIED, "no head in request frame")
	}

	req, err := http.NewRequestWithContext(ctx, frame.Head.Method, p.apiURL+frame.Head.Path, bytes.NewReader(frame.BodyChunk))
	if err != nil {
		return errorFrame(frame.RequestId, pb.TunnelErrorCode_TUNNEL_ERROR_UNSPECIFIED, err.Error())
	}

	// Forward headers, translate impersonation headers.
	for _, h := range frame.Head.Headers {
		lower := strings.ToLower(h.Name)

		if lower == "x-clustral-impersonate-user" {
			req.Header.Set("Impersonate-User", h.Value)
			continue
		}
		if lower == "x-clustral-impersonate-group" {
			// This is the whole reason for the Go rewrite:
			// Go's Header.Add() sends each value as a separate header line.
			req.Header.Add("Impersonate-Group", h.Value)
			continue
		}

		if hopByHopHeaders[lower] {
			continue
		}
		if strings.HasPrefix(lower, "x-clustral-") {
			continue
		}

		req.Header.Add(h.Name, h.Value)
	}

	resp, err := p.httpClient.Do(req)
	if err != nil {
		p.logger.Warn("k8s API request failed", "requestId", frame.RequestId, "path", frame.Head.Path, "error", err)
		return errorFrame(frame.RequestId, pb.TunnelErrorCode_TUNNEL_ERROR_API_SERVER_UNREACHABLE, err.Error())
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return errorFrame(frame.RequestId, pb.TunnelErrorCode_TUNNEL_ERROR_UNSPECIFIED, err.Error())
	}

	responseFrame := &pb.HttpResponseFrame{
		RequestId: frame.RequestId,
		Head: &pb.HttpResponseHead{
			StatusCode: int32(resp.StatusCode),
		},
		BodyChunk: body,
		EndOfBody: true,
	}

	for name, values := range resp.Header {
		responseFrame.Head.Headers = append(responseFrame.Head.Headers, &pb.HttpHeader{
			Name:  name,
			Value: strings.Join(values, ", "),
		})
	}

	return responseFrame
}

func errorFrame(requestID string, code pb.TunnelErrorCode, msg string) *pb.HttpResponseFrame {
	return &pb.HttpResponseFrame{
		RequestId: requestID,
		EndOfBody: true,
		Error: &pb.TunnelError{
			Code:    code,
			Message: msg,
		},
	}
}

func buildHTTPClient(apiURL string, skipTLSVerify bool) *http.Client {
	const saTokenPath = "/var/run/secrets/kubernetes.io/serviceaccount/token"
	const caCertPath = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt"

	tlsConfig := &tls.Config{}

	if skipTLSVerify {
		tlsConfig.InsecureSkipVerify = true
	} else if data, err := os.ReadFile(caCertPath); err == nil {
		pool := x509.NewCertPool()
		pool.AppendCertsFromPEM(data)
		tlsConfig.RootCAs = pool
	}

	transport := &http.Transport{TLSClientConfig: tlsConfig}

	// Wrap with SA token injector if running in-cluster.
	var rt http.RoundTripper = transport
	if _, err := os.Stat(saTokenPath); err == nil {
		rt = &saTokenRoundTripper{inner: transport, tokenPath: saTokenPath}
	}

	return &http.Client{Transport: rt}
}

// saTokenRoundTripper injects the ServiceAccount bearer token on every request.
// Re-reads the token file each time (kubelet rotates it hourly).
type saTokenRoundTripper struct {
	inner     http.RoundTripper
	tokenPath string
}

func (rt *saTokenRoundTripper) RoundTrip(req *http.Request) (*http.Response, error) {
	data, err := os.ReadFile(rt.tokenPath)
	if err != nil {
		return nil, err
	}
	req.Header.Set("Authorization", "Bearer "+strings.TrimSpace(string(data)))
	return rt.inner.RoundTrip(req)
}
