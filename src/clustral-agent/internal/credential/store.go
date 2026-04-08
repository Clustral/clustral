package credential

import (
	"crypto/tls"
	"crypto/x509"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

type Store struct {
	tokenPath  string
	expiryPath string
	mu         sync.Mutex
}

func NewStore(path string) *Store {
	return &Store{
		tokenPath:  path,
		expiryPath: path + ".expiry",
	}
}

func (s *Store) ReadToken() (string, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	data, err := os.ReadFile(s.tokenPath)
	if err != nil {
		if os.IsNotExist(err) {
			return "", nil
		}
		return "", err
	}
	token := strings.TrimSpace(string(data))
	if token == "" {
		return "", nil
	}
	return token, nil
}

func (s *Store) ReadExpiry() (time.Time, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	data, err := os.ReadFile(s.expiryPath)
	if err != nil {
		if os.IsNotExist(err) {
			return time.Time{}, nil
		}
		return time.Time{}, err
	}
	return time.Parse(time.RFC3339, strings.TrimSpace(string(data)))
}

func (s *Store) Save(token string, expiresAt time.Time) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	dir := filepath.Dir(s.tokenPath)
	if err := os.MkdirAll(dir, 0700); err != nil {
		return err
	}

	// Atomic write: temp file + rename.
	if err := writeAtomic(s.tokenPath, []byte(token)); err != nil {
		return err
	}
	return writeAtomic(s.expiryPath, []byte(expiresAt.Format(time.RFC3339)))
}

// SaveCertAndKey writes the client certificate and private key PEM to disk.
func (s *Store) SaveCertAndKey(certPEM, keyPEM []byte) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	dir := filepath.Dir(s.tokenPath)
	if err := os.MkdirAll(dir, 0700); err != nil {
		return err
	}

	certPath := filepath.Join(dir, "client.crt")
	keyPath := filepath.Join(dir, "client.key")

	if err := writeAtomic(certPath, certPEM); err != nil {
		return err
	}
	return writeAtomic(keyPath, keyPEM)
}

// ReadCert loads the client TLS certificate from disk.
func (s *Store) ReadCert() (tls.Certificate, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	dir := filepath.Dir(s.tokenPath)
	certPath := filepath.Join(dir, "client.crt")
	keyPath := filepath.Join(dir, "client.key")

	return tls.LoadX509KeyPair(certPath, keyPath)
}

// SaveCACert writes the CA certificate PEM to disk.
func (s *Store) SaveCACert(caPEM []byte) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	dir := filepath.Dir(s.tokenPath)
	if err := os.MkdirAll(dir, 0700); err != nil {
		return err
	}

	caPath := filepath.Join(dir, "ca.crt")
	return writeAtomic(caPath, caPEM)
}

// ReadCACert loads the CA certificate pool from disk.
func (s *Store) ReadCACert() (*x509.CertPool, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	dir := filepath.Dir(s.tokenPath)
	caPath := filepath.Join(dir, "ca.crt")

	pem, err := os.ReadFile(caPath)
	if err != nil {
		return nil, err
	}

	pool := x509.NewCertPool()
	if !pool.AppendCertsFromPEM(pem) {
		return nil, os.ErrInvalid
	}
	return pool, nil
}

// SaveJWT writes the JWT to disk.
func (s *Store) SaveJWT(jwt string) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	dir := filepath.Dir(s.tokenPath)
	if err := os.MkdirAll(dir, 0700); err != nil {
		return err
	}

	jwtPath := filepath.Join(dir, "agent.jwt")
	return writeAtomic(jwtPath, []byte(jwt))
}

// ReadJWT reads the JWT from disk.
func (s *Store) ReadJWT() (string, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	dir := filepath.Dir(s.tokenPath)
	jwtPath := filepath.Join(dir, "agent.jwt")

	data, err := os.ReadFile(jwtPath)
	if err != nil {
		if os.IsNotExist(err) {
			return "", nil
		}
		return "", err
	}
	return strings.TrimSpace(string(data)), nil
}

// HasMTLSCredentials checks if cert, key, CA cert, and JWT files exist.
func (s *Store) HasMTLSCredentials() bool {
	dir := filepath.Dir(s.tokenPath)
	for _, name := range []string{"client.crt", "client.key", "ca.crt", "agent.jwt"} {
		if _, err := os.Stat(filepath.Join(dir, name)); err != nil {
			return false
		}
	}
	return true
}

func writeAtomic(path string, data []byte) error {
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, data, 0600); err != nil {
		return err
	}
	return os.Rename(tmp, path)
}
