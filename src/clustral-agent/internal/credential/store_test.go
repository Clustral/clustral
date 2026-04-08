package credential

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"math/big"
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

// ── mTLS credential tests ───────────────────────────────────────────────────

func TestSaveCertAndKey_RoundTrip(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "agent.token"))

	certPEM, keyPEM := generateTestCert(t)

	if err := s.SaveCertAndKey(certPEM, keyPEM); err != nil {
		t.Fatalf("SaveCertAndKey error: %v", err)
	}

	cert, err := s.ReadCert()
	if err != nil {
		t.Fatalf("ReadCert error: %v", err)
	}

	if len(cert.Certificate) == 0 {
		t.Error("ReadCert should return at least one certificate")
	}
}

func TestReadCert_FileMissing_Error(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "agent.token"))

	_, err := s.ReadCert()
	if err == nil {
		t.Error("ReadCert should error when cert file missing")
	}
}

func TestSaveCACert_RoundTrip(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "agent.token"))

	caPEM := generateTestCACertPEM(t)

	if err := s.SaveCACert(caPEM); err != nil {
		t.Fatalf("SaveCACert error: %v", err)
	}

	pool, err := s.ReadCACert()
	if err != nil {
		t.Fatalf("ReadCACert error: %v", err)
	}

	if pool == nil {
		t.Error("ReadCACert should return a non-nil pool")
	}
}

func TestReadCACert_InvalidPEM_Error(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "agent.token"))

	// Write invalid PEM.
	caPath := filepath.Join(dir, "ca.crt")
	if err := os.WriteFile(caPath, []byte("not-a-cert"), 0600); err != nil {
		t.Fatalf("write error: %v", err)
	}

	_, err := s.ReadCACert()
	if err == nil {
		t.Error("ReadCACert should error on invalid PEM")
	}
}

func TestSaveJWT_RoundTrip(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "agent.token"))

	jwt := "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ0ZXN0In0.signature"

	if err := s.SaveJWT(jwt); err != nil {
		t.Fatalf("SaveJWT error: %v", err)
	}

	got, err := s.ReadJWT()
	if err != nil {
		t.Fatalf("ReadJWT error: %v", err)
	}
	if got != jwt {
		t.Errorf("ReadJWT = %q, want %q", got, jwt)
	}
}

func TestReadJWT_FileMissing_Empty(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "agent.token"))

	got, err := s.ReadJWT()
	if err != nil {
		t.Fatalf("ReadJWT error: %v", err)
	}
	if got != "" {
		t.Errorf("ReadJWT = %q, want empty", got)
	}
}

func TestHasMTLSCredentials_AllPresent_True(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "agent.token"))

	certPEM, keyPEM := generateTestCert(t)
	caPEM := generateTestCACertPEM(t)

	_ = s.SaveCertAndKey(certPEM, keyPEM)
	_ = s.SaveCACert(caPEM)
	_ = s.SaveJWT("test-jwt")

	if !s.HasMTLSCredentials() {
		t.Error("HasMTLSCredentials should be true when all files present")
	}
}

func TestHasMTLSCredentials_Missing_False(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "agent.token"))

	if s.HasMTLSCredentials() {
		t.Error("HasMTLSCredentials should be false when no files exist")
	}
}

// ── helpers ─────────────────────────────────────────────────────────────────

func generateTestCert(t *testing.T) (certPEM, keyPEM []byte) {
	t.Helper()

	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatalf("generate key: %v", err)
	}

	template := &x509.Certificate{
		SerialNumber: big.NewInt(1),
		Subject:      pkix.Name{CommonName: "test-agent"},
		NotBefore:    time.Now(),
		NotAfter:     time.Now().Add(24 * time.Hour),
	}

	certDER, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		t.Fatalf("create cert: %v", err)
	}

	certPEM = pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: certDER})

	keyDER, err := x509.MarshalECPrivateKey(key)
	if err != nil {
		t.Fatalf("marshal key: %v", err)
	}
	keyPEM = pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: keyDER})

	return certPEM, keyPEM
}

func generateTestCACertPEM(t *testing.T) []byte {
	t.Helper()

	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatalf("generate key: %v", err)
	}

	template := &x509.Certificate{
		SerialNumber: big.NewInt(1),
		Subject:      pkix.Name{CommonName: "test-ca"},
		NotBefore:    time.Now(),
		NotAfter:     time.Now().Add(24 * time.Hour),
		IsCA:         true,
		KeyUsage:     x509.KeyUsageCertSign,
	}

	certDER, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		t.Fatalf("create cert: %v", err)
	}

	return pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: certDER})
}
