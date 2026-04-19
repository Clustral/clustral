package auth

import (
	"crypto/rand"
	"crypto/rsa"
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func generateTestKey(t *testing.T) *rsa.PrivateKey {
	t.Helper()
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	require.NoError(t, err)
	return key
}

func issueTestJWT(t *testing.T, key *rsa.PrivateKey, claims jwt.MapClaims) string {
	t.Helper()
	token := jwt.NewWithClaims(jwt.SigningMethodRS256, claims)
	signed, err := token.SignedString(key)
	require.NoError(t, err)
	return signed
}

func validClaims() jwt.MapClaims {
	return jwt.MapClaims{
		"iss":          "clustral-controlplane",
		"agent_id":     "agent-1",
		"cluster_id":   "cluster-1",
		"tokenVersion": 1,
		"allowedRpcs":  []string{"TunnelService/OpenTunnel"},
		"exp":          jwt.NewNumericDate(time.Now().Add(1 * time.Hour)),
		"nbf":          jwt.NewNumericDate(time.Now().Add(-1 * time.Minute)),
		"iat":          jwt.NewNumericDate(time.Now()),
	}
}

func TestJWTValidator_ValidToken(t *testing.T) {
	key := generateTestKey(t)
	validator := NewJWTValidatorFromKey(&key.PublicKey)

	token := issueTestJWT(t, key, validClaims())
	claims, err := validator.Validate(token)

	require.NoError(t, err)
	assert.Equal(t, "agent-1", claims.AgentID)
	assert.Equal(t, "cluster-1", claims.ClusterID)
	assert.Equal(t, 1, claims.TokenVersion)
	assert.Contains(t, claims.AllowedRPCs, "TunnelService/OpenTunnel")
}

func TestJWTValidator_ExpiredToken(t *testing.T) {
	key := generateTestKey(t)
	validator := NewJWTValidatorFromKey(&key.PublicKey)

	c := validClaims()
	c["exp"] = jwt.NewNumericDate(time.Now().Add(-1 * time.Hour))
	token := issueTestJWT(t, key, c)

	_, err := validator.Validate(token)
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "token is expired")
}

func TestJWTValidator_WrongKey(t *testing.T) {
	signingKey := generateTestKey(t)
	wrongKey := generateTestKey(t)
	validator := NewJWTValidatorFromKey(&wrongKey.PublicKey)

	token := issueTestJWT(t, signingKey, validClaims())

	_, err := validator.Validate(token)
	assert.Error(t, err)
}

func TestJWTValidator_WrongIssuer(t *testing.T) {
	key := generateTestKey(t)
	validator := NewJWTValidatorFromKey(&key.PublicKey)

	c := validClaims()
	c["iss"] = "wrong-issuer"
	token := issueTestJWT(t, key, c)

	_, err := validator.Validate(token)
	assert.Error(t, err)
}

func TestJWTValidator_MissingClaims(t *testing.T) {
	key := generateTestKey(t)
	validator := NewJWTValidatorFromKey(&key.PublicKey)

	// Token with no agent_id or cluster_id — should still parse but with empty values.
	c := jwt.MapClaims{
		"iss": "clustral-controlplane",
		"exp": jwt.NewNumericDate(time.Now().Add(1 * time.Hour)),
	}
	token := issueTestJWT(t, key, c)

	claims, err := validator.Validate(token)
	require.NoError(t, err)
	assert.Empty(t, claims.AgentID)
	assert.Empty(t, claims.ClusterID)
}
