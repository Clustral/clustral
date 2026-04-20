package cluster

import (
	"crypto/rsa"
	"fmt"
	"time"

	"github.com/golang-jwt/jwt/v5"
)

const jwtIssuer = "clustral-controlplane"

// JWTSigner issues RS256 JWTs for agent authentication.
type JWTSigner struct {
	privateKey   *rsa.PrivateKey
	validityDays int
}

// NewJWTSigner creates a new JWT signer from an RSA private key.
func NewJWTSigner(key *rsa.PrivateKey, validityDays int) *JWTSigner {
	return &JWTSigner{
		privateKey:   key,
		validityDays: validityDays,
	}
}

// NewJWTSignerFromCA creates a JWT signer from a CA's private key.
// Returns an error if the CA key is not RSA.
func NewJWTSignerFromCA(ca *CA, validityDays int) (*JWTSigner, error) {
	rsaKey, ok := ca.key.(*rsa.PrivateKey)
	if !ok {
		return nil, fmt.Errorf("JWT signing requires an RSA private key, got %T", ca.key)
	}
	return &JWTSigner{
		privateKey:   rsaKey,
		validityDays: validityDays,
	}, nil
}

// IssueToken creates a JWT for the given agent.
func (s *JWTSigner) IssueToken(agentID, orgID, clusterID string, allowedRPCs []string, tokenVersion int) (string, error) {
	now := time.Now()

	claims := jwt.MapClaims{
		"iss":          jwtIssuer,
		"agent_id":     agentID,
		"org_id":       orgID,
		"cluster_id":   clusterID,
		"tokenVersion": tokenVersion,
		"allowedRpcs":  allowedRPCs,
		"nbf":          jwt.NewNumericDate(now),
		"exp":          jwt.NewNumericDate(now.AddDate(0, 0, s.validityDays)),
		"iat":          jwt.NewNumericDate(now),
	}

	token := jwt.NewWithClaims(jwt.SigningMethodRS256, claims)
	return token.SignedString(s.privateKey)
}

// GetTokenExpiry returns the expiry time for newly issued tokens.
func (s *JWTSigner) GetTokenExpiry() time.Time {
	return time.Now().AddDate(0, 0, s.validityDays)
}

// PublicKey returns the public key for validation.
func (s *JWTSigner) PublicKey() *rsa.PublicKey {
	return &s.privateKey.PublicKey
}
