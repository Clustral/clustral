package credential

import (
	"os"
	"path/filepath"
	"sync"
	"testing"
	"time"
)

func TestNewStore(t *testing.T) {
	s := NewStore("/tmp/test.token")
	if s.tokenPath != "/tmp/test.token" {
		t.Errorf("tokenPath = %q, want /tmp/test.token", s.tokenPath)
	}
	if s.expiryPath != "/tmp/test.token.expiry" {
		t.Errorf("expiryPath = %q, want /tmp/test.token.expiry", s.expiryPath)
	}
}

func TestReadToken_FileExists(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "token")
	os.WriteFile(path, []byte("  my-token  \n"), 0600)

	s := NewStore(path)
	token, err := s.ReadToken()
	if err != nil {
		t.Fatalf("ReadToken error: %v", err)
	}
	if token != "my-token" {
		t.Errorf("ReadToken = %q, want %q", token, "my-token")
	}
}

func TestReadToken_FileMissing(t *testing.T) {
	s := NewStore(filepath.Join(t.TempDir(), "nonexistent"))
	token, err := s.ReadToken()
	if err != nil {
		t.Fatalf("ReadToken error: %v", err)
	}
	if token != "" {
		t.Errorf("ReadToken missing file should return empty, got %q", token)
	}
}

func TestReadToken_WhitespaceOnly(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "token")
	os.WriteFile(path, []byte("   \n  \t  "), 0600)

	s := NewStore(path)
	token, err := s.ReadToken()
	if err != nil {
		t.Fatalf("ReadToken error: %v", err)
	}
	if token != "" {
		t.Errorf("ReadToken whitespace should return empty, got %q", token)
	}
}

func TestReadExpiry_ValidRFC3339(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "token")
	expiryPath := path + ".expiry"
	expected := time.Date(2099, 1, 1, 0, 0, 0, 0, time.UTC)
	os.WriteFile(expiryPath, []byte(expected.Format(time.RFC3339)), 0600)

	s := NewStore(path)
	expiry, err := s.ReadExpiry()
	if err != nil {
		t.Fatalf("ReadExpiry error: %v", err)
	}
	if !expiry.Equal(expected) {
		t.Errorf("ReadExpiry = %v, want %v", expiry, expected)
	}
}

func TestReadExpiry_FileMissing(t *testing.T) {
	s := NewStore(filepath.Join(t.TempDir(), "nonexistent"))
	expiry, err := s.ReadExpiry()
	if err != nil {
		t.Fatalf("ReadExpiry error: %v", err)
	}
	if !expiry.IsZero() {
		t.Errorf("ReadExpiry missing file should return zero time, got %v", expiry)
	}
}

func TestReadExpiry_InvalidFormat(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "token")
	expiryPath := path + ".expiry"
	os.WriteFile(expiryPath, []byte("not-a-date"), 0600)

	s := NewStore(path)
	_, err := s.ReadExpiry()
	if err == nil {
		t.Error("ReadExpiry with invalid format should return error")
	}
}

func TestSave_RoundTrip(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "sub", "token")

	s := NewStore(path)
	expiry := time.Date(2099, 6, 15, 12, 0, 0, 0, time.UTC)

	if err := s.Save("test-jwt", expiry); err != nil {
		t.Fatalf("Save error: %v", err)
	}

	// Read back.
	token, err := s.ReadToken()
	if err != nil {
		t.Fatalf("ReadToken error: %v", err)
	}
	if token != "test-jwt" {
		t.Errorf("ReadToken = %q, want test-jwt", token)
	}

	readExpiry, err := s.ReadExpiry()
	if err != nil {
		t.Fatalf("ReadExpiry error: %v", err)
	}
	if !readExpiry.Equal(expiry) {
		t.Errorf("ReadExpiry = %v, want %v", readExpiry, expiry)
	}
}

func TestSave_CreatesDirectory(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "deep", "nested", "token")

	s := NewStore(path)
	if err := s.Save("tok", time.Now()); err != nil {
		t.Fatalf("Save error: %v", err)
	}

	if _, err := os.Stat(path); os.IsNotExist(err) {
		t.Error("Save should create parent directories")
	}
}

func TestSave_FilePermissions(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "token")

	s := NewStore(path)
	if err := s.Save("tok", time.Now()); err != nil {
		t.Fatalf("Save error: %v", err)
	}

	info, err := os.Stat(path)
	if err != nil {
		t.Fatalf("Stat error: %v", err)
	}
	mode := info.Mode().Perm()
	if mode != 0600 {
		t.Errorf("File permissions = %o, want 0600", mode)
	}
}

func TestSave_ConcurrentAccess(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "token")
	s := NewStore(path)

	var wg sync.WaitGroup
	for i := 0; i < 10; i++ {
		wg.Add(1)
		go func(n int) {
			defer wg.Done()
			_ = s.Save("token-"+string(rune('A'+n)), time.Now().Add(time.Duration(n)*time.Hour))
		}(i)
	}
	wg.Wait()

	// Should not crash or corrupt — just verify readable.
	token, err := s.ReadToken()
	if err != nil {
		t.Fatalf("ReadToken after concurrent writes: %v", err)
	}
	if token == "" {
		t.Error("Token should not be empty after concurrent writes")
	}
}
