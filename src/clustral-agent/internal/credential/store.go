package credential

import (
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

func writeAtomic(path string, data []byte) error {
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, data, 0600); err != nil {
		return err
	}
	return os.Rename(tmp, path)
}
