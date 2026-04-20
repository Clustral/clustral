package auth

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/rsa"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"log/slog"
	"math/big"
	"os"
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/credentials"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/peer"
	"google.golang.org/grpc/status"
)

func newTestInterceptor(t *testing.T, rsaKey *rsa.PrivateKey) *AuthInterceptor {
	t.Helper()
	validator := NewJWTValidatorFromKey(&rsaKey.PublicKey)
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelError}))
	lookup := func(ctx context.Context, clusterID string) (int, error) {
		return 1, nil
	}
	return NewAuthInterceptor(validator, lookup, logger)
}

func makePeerContext(t *testing.T, cn string, token string) context.Context {
	t.Helper()

	// Create a fake TLS peer with a client cert.
	clientKey, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	require.NoError(t, err)

	template := &x509.Certificate{
		SerialNumber: big.NewInt(1),
		Subject:      pkix.Name{CommonName: cn},
		NotBefore:    time.Now().Add(-1 * time.Hour),
		NotAfter:     time.Now().Add(24 * time.Hour),
	}
	certDER, err := x509.CreateCertificate(rand.Reader, template, template, &clientKey.PublicKey, clientKey)
	require.NoError(t, err)

	cert, err := x509.ParseCertificate(certDER)
	require.NoError(t, err)

	tlsInfo := credentials.TLSInfo{
		State: tls.ConnectionState{
			PeerCertificates: []*x509.Certificate{cert},
		},
	}

	ctx := peer.NewContext(context.Background(), &peer.Peer{
		AuthInfo: tlsInfo,
	})

	if token != "" {
		ctx = metadata.NewIncomingContext(ctx, metadata.Pairs("authorization", "Bearer "+token))
	}

	return ctx
}

func TestInterceptor_ValidAuth(t *testing.T) {
	rsaKey := generateTestKey(t)
	interceptor := newTestInterceptor(t, rsaKey)

	token := issueTestJWT(t, rsaKey, jwt.MapClaims{
		"iss":          "clustral-controlplane",
		"agent_id":     "agent-1",
		"cluster_id":   "cluster-1",
		"tokenVersion": 1,
		"allowedRpcs":  []string{"ClusterService/Get"},
		"exp":          jwt.NewNumericDate(time.Now().Add(1 * time.Hour)),
		"nbf":          jwt.NewNumericDate(time.Now().Add(-1 * time.Minute)),
	})

	ctx := makePeerContext(t, "agent-1", token)
	newCtx, err := interceptor.validate(ctx, "/clustral.v1.ClusterService/Get")
	require.NoError(t, err)

	identity, ok := GetAgentIdentity(newCtx)
	require.True(t, ok)
	assert.Equal(t, "agent-1", identity.AgentID)
	assert.Equal(t, "cluster-1", identity.ClusterID)
}

func TestInterceptor_RegisterAgent_SkipsAuth(t *testing.T) {
	rsaKey := generateTestKey(t)
	interceptor := newTestInterceptor(t, rsaKey)

	// No peer info, no token — should pass for bootstrap RPC.
	ctx := context.Background()
	_, err := interceptor.validate(ctx, "/clustral.v1.ClusterService/RegisterAgent")
	require.NoError(t, err)
}

func TestInterceptor_MismatchedCN(t *testing.T) {
	rsaKey := generateTestKey(t)
	interceptor := newTestInterceptor(t, rsaKey)

	token := issueTestJWT(t, rsaKey, jwt.MapClaims{
		"iss":          "clustral-controlplane",
		"agent_id":     "wrong-agent",
		"cluster_id":   "cluster-1",
		"tokenVersion": 1,
		"allowedRpcs":  []string{"ClusterService/Get"},
		"exp":          jwt.NewNumericDate(time.Now().Add(1 * time.Hour)),
		"nbf":          jwt.NewNumericDate(time.Now().Add(-1 * time.Minute)),
	})

	ctx := makePeerContext(t, "agent-1", token)
	_, err := interceptor.validate(ctx, "/clustral.v1.ClusterService/Get")

	st, _ := status.FromError(err)
	assert.Equal(t, codes.Unauthenticated, st.Code())
	assert.Contains(t, st.Message(), "does not match")
}

func TestInterceptor_NoJWT(t *testing.T) {
	rsaKey := generateTestKey(t)
	interceptor := newTestInterceptor(t, rsaKey)

	ctx := makePeerContext(t, "agent-1", "")
	_, err := interceptor.validate(ctx, "/clustral.v1.ClusterService/Get")

	st, _ := status.FromError(err)
	assert.Equal(t, codes.Unauthenticated, st.Code())
}

func TestInterceptor_NoCert(t *testing.T) {
	rsaKey := generateTestKey(t)
	interceptor := newTestInterceptor(t, rsaKey)

	// Peer with no TLS info.
	ctx := peer.NewContext(context.Background(), &peer.Peer{})
	ctx = metadata.NewIncomingContext(ctx, metadata.Pairs("authorization", "Bearer some-token"))

	_, err := interceptor.validate(ctx, "/clustral.v1.ClusterService/Get")

	st, _ := status.FromError(err)
	assert.Equal(t, codes.Unauthenticated, st.Code())
}

func TestInterceptor_RevokedToken(t *testing.T) {
	rsaKey := generateTestKey(t)
	validator := NewJWTValidatorFromKey(&rsaKey.PublicKey)
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelError}))

	// Token version in DB is 5, JWT has version 1 -> revoked.
	lookup := func(ctx context.Context, clusterID string) (int, error) {
		return 5, nil
	}
	interceptor := NewAuthInterceptor(validator, lookup, logger)

	token := issueTestJWT(t, rsaKey, jwt.MapClaims{
		"iss":          "clustral-controlplane",
		"agent_id":     "agent-1",
		"cluster_id":   "cluster-1",
		"tokenVersion": 1,
		"allowedRpcs":  []string{"ClusterService/Get"},
		"exp":          jwt.NewNumericDate(time.Now().Add(1 * time.Hour)),
		"nbf":          jwt.NewNumericDate(time.Now().Add(-1 * time.Minute)),
	})

	ctx := makePeerContext(t, "agent-1", token)
	_, err := interceptor.validate(ctx, "/clustral.v1.ClusterService/Get")

	st, _ := status.FromError(err)
	assert.Equal(t, codes.Unauthenticated, st.Code())
	assert.Contains(t, st.Message(), "revoked")
}

func TestInterceptor_RPCNotAllowed(t *testing.T) {
	rsaKey := generateTestKey(t)
	interceptor := newTestInterceptor(t, rsaKey)

	token := issueTestJWT(t, rsaKey, jwt.MapClaims{
		"iss":          "clustral-controlplane",
		"agent_id":     "agent-1",
		"cluster_id":   "cluster-1",
		"tokenVersion": 1,
		"allowedRpcs":  []string{"TunnelService/OpenTunnel"},
		"exp":          jwt.NewNumericDate(time.Now().Add(1 * time.Hour)),
		"nbf":          jwt.NewNumericDate(time.Now().Add(-1 * time.Minute)),
	})

	ctx := makePeerContext(t, "agent-1", token)
	_, err := interceptor.validate(ctx, "/clustral.v1.ClusterService/Get")

	st, _ := status.FromError(err)
	assert.Equal(t, codes.PermissionDenied, st.Code())
	assert.Contains(t, st.Message(), "not permitted")
}
