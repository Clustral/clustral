package auth

import (
	"crypto/rsa"
	"crypto/x509"
	"encoding/pem"
	"fmt"

	"github.com/golang-jwt/jwt/v5"
)

// AgentClaims holds the validated claims from an agent JWT.
type AgentClaims struct {
	AgentID      string   `json:"agent_id"`
	ClusterID    string   `json:"cluster_id"`
	TokenVersion int      `json:"tokenVersion"`
	AllowedRPCs  []string `json:"allowedRpcs"`
	jwt.RegisteredClaims
}

// JWTValidator validates RS256 agent JWTs.
type JWTValidator struct {
	publicKey *rsa.PublicKey
}

// NewJWTValidator parses an RSA public key from PEM and returns a validator.
func NewJWTValidator(publicKeyPEM string) (*JWTValidator, error) {
	block, _ := pem.Decode([]byte(publicKeyPEM))
	if block == nil {
		return nil, fmt.Errorf("failed to decode PEM block")
	}

	key, err := x509.ParsePKIXPublicKey(block.Bytes)
	if err != nil {
		return nil, fmt.Errorf("parse public key: %w", err)
	}

	rsaKey, ok := key.(*rsa.PublicKey)
	if !ok {
		return nil, fmt.Errorf("key is not RSA")
	}

	return &JWTValidator{publicKey: rsaKey}, nil
}

// NewJWTValidatorFromKey creates a validator from an existing RSA public key.
func NewJWTValidatorFromKey(key *rsa.PublicKey) *JWTValidator {
	return &JWTValidator{publicKey: key}
}

// Validate parses and validates a JWT string, returning the agent claims.
func (v *JWTValidator) Validate(tokenString string) (*AgentClaims, error) {
	claims := &AgentClaims{}

	token, err := jwt.ParseWithClaims(tokenString, claims, func(t *jwt.Token) (interface{}, error) {
		if _, ok := t.Method.(*jwt.SigningMethodRSA); !ok {
			return nil, fmt.Errorf("unexpected signing method: %v", t.Header["alg"])
		}
		return v.publicKey, nil
	},
		jwt.WithValidMethods([]string{"RS256"}),
		jwt.WithIssuer("clustral-controlplane"),
		jwt.WithExpirationRequired(),
	)
	if err != nil {
		return nil, fmt.Errorf("validate JWT: %w", err)
	}

	if !token.Valid {
		return nil, fmt.Errorf("invalid token")
	}

	return claims, nil
}
