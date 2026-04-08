package auth

import (
	"context"
	"sync"
	"testing"
)

func TestJWTCredentials_GetRequestMetadata(t *testing.T) {
	creds := NewJWTCredentials("test-jwt-token")

	md, err := creds.GetRequestMetadata(context.Background())
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	want := "Bearer test-jwt-token"
	if got := md["authorization"]; got != want {
		t.Errorf("authorization = %q, want %q", got, want)
	}
}

func TestJWTCredentials_Update(t *testing.T) {
	creds := NewJWTCredentials("old-token")

	creds.Update("new-token")

	md, _ := creds.GetRequestMetadata(context.Background())
	want := "Bearer new-token"
	if got := md["authorization"]; got != want {
		t.Errorf("authorization = %q, want %q", got, want)
	}
}

func TestJWTCredentials_Token(t *testing.T) {
	creds := NewJWTCredentials("my-jwt")

	if got := creds.Token(); got != "my-jwt" {
		t.Errorf("Token() = %q, want %q", got, "my-jwt")
	}
}

func TestJWTCredentials_RequireTransportSecurity(t *testing.T) {
	creds := NewJWTCredentials("token")

	if !creds.RequireTransportSecurity() {
		t.Error("RequireTransportSecurity() should return true")
	}
}

func TestJWTCredentials_ConcurrentAccess(t *testing.T) {
	creds := NewJWTCredentials("initial")

	var wg sync.WaitGroup
	for i := 0; i < 100; i++ {
		wg.Add(2)
		go func() {
			defer wg.Done()
			creds.Update("updated")
		}()
		go func() {
			defer wg.Done()
			_, _ = creds.GetRequestMetadata(context.Background())
		}()
	}
	wg.Wait()

	// Should not panic or race — verified by -race flag
	token := creds.Token()
	if token != "initial" && token != "updated" {
		t.Errorf("unexpected token: %q", token)
	}
}
