package auth

import (
	"context"
	"fmt"
	"log/slog"
	"strings"
	"sync"
	"time"

	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/credentials"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/peer"
	"google.golang.org/grpc/status"
)

// AgentIdentity holds the validated agent identity stored in context.
type AgentIdentity struct {
	AgentID   string
	ClusterID string
}

type contextKey string

const agentIdentityKey contextKey = "agentIdentity"

// GetAgentIdentity retrieves the validated agent identity from context.
func GetAgentIdentity(ctx context.Context) (*AgentIdentity, bool) {
	id, ok := ctx.Value(agentIdentityKey).(*AgentIdentity)
	return id, ok
}

// TokenVersionLookup is a function that returns the current token version for a cluster.
type TokenVersionLookup func(ctx context.Context, clusterID string) (int, error)

// AuthInterceptor provides gRPC server interceptors for agent mTLS + JWT auth.
type AuthInterceptor struct {
	validator          *JWTValidator
	tokenVersionLookup TokenVersionLookup
	logger             *slog.Logger

	// Token version cache: clusterID -> {version, fetchedAt}
	cacheMu sync.RWMutex
	cache   map[string]cachedVersion
}

type cachedVersion struct {
	version  int
	fetchedAt time.Time
}

const tokenVersionCacheTTL = 30 * time.Second

// bootstrapRPCs are methods that skip auth (agent has no cert yet).
var bootstrapRPCs = map[string]bool{
	"/clustral.v1.ClusterService/RegisterAgent": true,
}

// NewAuthInterceptor creates a new auth interceptor.
func NewAuthInterceptor(validator *JWTValidator, lookup TokenVersionLookup, logger *slog.Logger) *AuthInterceptor {
	return &AuthInterceptor{
		validator:          validator,
		tokenVersionLookup: lookup,
		logger:             logger,
		cache:              make(map[string]cachedVersion),
	}
}

// UnaryInterceptor returns a gRPC unary server interceptor.
func (a *AuthInterceptor) UnaryInterceptor() grpc.UnaryServerInterceptor {
	return func(ctx context.Context, req interface{}, info *grpc.UnaryServerInfo, handler grpc.UnaryHandler) (interface{}, error) {
		newCtx, err := a.validate(ctx, info.FullMethod)
		if err != nil {
			return nil, err
		}
		return handler(newCtx, req)
	}
}

// StreamInterceptor returns a gRPC stream server interceptor.
func (a *AuthInterceptor) StreamInterceptor() grpc.StreamServerInterceptor {
	return func(srv interface{}, ss grpc.ServerStream, info *grpc.StreamServerInfo, handler grpc.StreamHandler) error {
		newCtx, err := a.validate(ss.Context(), info.FullMethod)
		if err != nil {
			return err
		}
		return handler(srv, &wrappedStream{ServerStream: ss, ctx: newCtx})
	}
}

func (a *AuthInterceptor) validate(ctx context.Context, method string) (context.Context, error) {
	// Bootstrap RPCs skip auth.
	if bootstrapRPCs[method] {
		return ctx, nil
	}

	// 1. Extract client certificate CN.
	p, ok := peer.FromContext(ctx)
	if !ok {
		return ctx, status.Error(codes.Unauthenticated, "no peer info")
	}

	tlsInfo, ok := p.AuthInfo.(credentials.TLSInfo)
	if !ok || len(tlsInfo.State.PeerCertificates) == 0 {
		return ctx, status.Error(codes.Unauthenticated, "client certificate required")
	}

	certCN := tlsInfo.State.PeerCertificates[0].Subject.CommonName

	// 2. Extract JWT from authorization metadata.
	md, ok := metadata.FromIncomingContext(ctx)
	if !ok {
		return ctx, status.Error(codes.Unauthenticated, "missing metadata")
	}

	authValues := md.Get("authorization")
	if len(authValues) == 0 || !strings.HasPrefix(authValues[0], "Bearer ") {
		return ctx, status.Error(codes.Unauthenticated, "Bearer JWT required in authorization header")
	}
	tokenStr := strings.TrimPrefix(authValues[0], "Bearer ")

	// 3. Validate JWT.
	claims, err := a.validator.Validate(tokenStr)
	if err != nil {
		a.logger.Warn("JWT validation failed", "method", method, "error", err)
		return ctx, status.Error(codes.Unauthenticated, "invalid or expired JWT")
	}

	// 4. Cross-check: cert CN must match JWT agent_id.
	if !strings.EqualFold(claims.AgentID, certCN) {
		a.logger.Warn("JWT agent_id does not match cert CN",
			"jwtAgentId", claims.AgentID, "certCN", certCN)
		return ctx, status.Error(codes.Unauthenticated, "JWT agent_id does not match certificate CN")
	}

	// 5. Check tokenVersion against stored version (cached).
	storedVersion, err := a.getCachedTokenVersion(ctx, claims.ClusterID)
	if err != nil {
		a.logger.Warn("Failed to lookup token version", "clusterID", claims.ClusterID, "error", err)
		return ctx, status.Error(codes.Unauthenticated, "failed to verify token version")
	}

	if claims.TokenVersion < storedVersion {
		a.logger.Warn("Token version mismatch",
			"clusterID", claims.ClusterID,
			"jwtVersion", claims.TokenVersion,
			"storedVersion", storedVersion)
		return ctx, status.Error(codes.Unauthenticated, "token has been revoked (version mismatch)")
	}

	// 6. Check allowedRpcs.
	if len(claims.AllowedRPCs) > 0 {
		fullMethod := strings.TrimPrefix(method, "/")
		allowed := false
		for _, rpc := range claims.AllowedRPCs {
			if strings.Contains(fullMethod, rpc) {
				allowed = true
				break
			}
		}
		if !allowed {
			shortMethod := method
			if parts := strings.Split(method, "/"); len(parts) > 0 {
				shortMethod = parts[len(parts)-1]
			}
			a.logger.Warn("RPC not permitted", "method", method, "agentId", claims.AgentID)
			return ctx, status.Errorf(codes.PermissionDenied, "RPC %s is not permitted for this agent", shortMethod)
		}
	}

	// Store identity in context.
	newCtx := context.WithValue(ctx, agentIdentityKey, &AgentIdentity{
		AgentID:   claims.AgentID,
		ClusterID: claims.ClusterID,
	})

	return newCtx, nil
}

func (a *AuthInterceptor) getCachedTokenVersion(ctx context.Context, clusterID string) (int, error) {
	a.cacheMu.RLock()
	if cached, ok := a.cache[clusterID]; ok && time.Since(cached.fetchedAt) < tokenVersionCacheTTL {
		a.cacheMu.RUnlock()
		return cached.version, nil
	}
	a.cacheMu.RUnlock()

	version, err := a.tokenVersionLookup(ctx, clusterID)
	if err != nil {
		return 0, fmt.Errorf("lookup token version: %w", err)
	}

	a.cacheMu.Lock()
	a.cache[clusterID] = cachedVersion{version: version, fetchedAt: time.Now()}
	a.cacheMu.Unlock()

	return version, nil
}

// wrappedStream wraps a grpc.ServerStream with a new context.
type wrappedStream struct {
	grpc.ServerStream
	ctx context.Context
}

func (w *wrappedStream) Context() context.Context {
	return w.ctx
}
