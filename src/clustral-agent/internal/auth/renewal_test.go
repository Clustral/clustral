package auth

import (
	"encoding/base64"
	"encoding/json"
	"testing"
	"time"
)

func TestParseJWTExpiry_ValidJWT(t *testing.T) {
	// Build a fake JWT with a known exp claim
	exp := time.Now().Add(24 * time.Hour).Unix()
	payload := map[string]interface{}{"exp": exp, "agent_id": "test"}
	payloadJSON, _ := json.Marshal(payload)
	fakeJWT := "eyJhbGciOiJSUzI1NiJ9." + base64.RawURLEncoding.EncodeToString(payloadJSON) + ".fakesig"

	got, err := parseJWTExpiry(fakeJWT)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	// Should be within 1 second of the expected time
	diff := got.Sub(time.Unix(exp, 0))
	if diff < -time.Second || diff > time.Second {
		t.Errorf("expiry diff = %v, want ~0", diff)
	}
}

func TestParseJWTExpiry_NoExpClaim(t *testing.T) {
	payload := map[string]interface{}{"agent_id": "test"}
	payloadJSON, _ := json.Marshal(payload)
	fakeJWT := "eyJhbGciOiJSUzI1NiJ9." + base64.RawURLEncoding.EncodeToString(payloadJSON) + ".fakesig"

	_, err := parseJWTExpiry(fakeJWT)
	if err == nil {
		t.Fatal("expected error for missing exp claim")
	}
}

func TestParseJWTExpiry_InvalidFormat(t *testing.T) {
	_, err := parseJWTExpiry("not-a-jwt")
	if err == nil {
		t.Fatal("expected error for invalid JWT format")
	}
}

func TestParseJWTExpiry_InvalidBase64(t *testing.T) {
	_, err := parseJWTExpiry("header.!!!invalid!!!.signature")
	if err == nil {
		t.Fatal("expected error for invalid base64")
	}
}
