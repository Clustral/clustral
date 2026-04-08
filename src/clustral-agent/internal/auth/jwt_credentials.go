package auth

import (
	"context"
	"sync"
)

// JWTCredentials implements grpc/credentials.PerRPCCredentials.
// It provides a thread-safe JWT that can be swapped at runtime
// when the RenewalManager renews the token.
type JWTCredentials struct {
	mu  sync.RWMutex
	jwt string
}

// NewJWTCredentials creates a new JWTCredentials with the given initial JWT.
func NewJWTCredentials(jwt string) *JWTCredentials {
	return &JWTCredentials{jwt: jwt}
}

// GetRequestMetadata returns the authorization header with the current JWT.
func (j *JWTCredentials) GetRequestMetadata(ctx context.Context, uri ...string) (map[string]string, error) {
	j.mu.RLock()
	defer j.mu.RUnlock()
	return map[string]string{
		"authorization": "Bearer " + j.jwt,
	}, nil
}

// RequireTransportSecurity returns true — JWT must only be sent over TLS.
func (j *JWTCredentials) RequireTransportSecurity() bool {
	return true
}

// Update atomically swaps the JWT token.
func (j *JWTCredentials) Update(jwt string) {
	j.mu.Lock()
	defer j.mu.Unlock()
	j.jwt = jwt
}

// Token returns the current JWT (for reading expiry, etc.).
func (j *JWTCredentials) Token() string {
	j.mu.RLock()
	defer j.mu.RUnlock()
	return j.jwt
}
