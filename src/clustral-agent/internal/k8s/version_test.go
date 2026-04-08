package k8s

import (
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestDiscoverVersion_Success(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/version" {
			t.Errorf("unexpected path: %s", r.URL.Path)
		}
		w.Header().Set("Content-Type", "application/json")
		w.Write([]byte(`{
			"major": "1",
			"minor": "29",
			"gitVersion": "v1.29.0",
			"gitCommit": "abc123",
			"buildDate": "2024-01-01T00:00:00Z",
			"goVersion": "go1.21.5",
			"compiler": "gc",
			"platform": "linux/amd64"
		}`))
	}))
	defer srv.Close()

	version := DiscoverVersion(srv.URL, srv.Client())

	if version != "v1.29.0" {
		t.Errorf("expected v1.29.0, got %s", version)
	}
}

func TestDiscoverVersion_ServerError(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusInternalServerError)
	}))
	defer srv.Close()

	version := DiscoverVersion(srv.URL, srv.Client())

	if version != "unknown" {
		t.Errorf("expected unknown, got %s", version)
	}
}

func TestDiscoverVersion_MalformedJSON(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte(`not json`))
	}))
	defer srv.Close()

	version := DiscoverVersion(srv.URL, srv.Client())

	if version != "unknown" {
		t.Errorf("expected unknown, got %s", version)
	}
}

func TestDiscoverVersion_EmptyGitVersion(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte(`{"gitVersion": ""}`))
	}))
	defer srv.Close()

	version := DiscoverVersion(srv.URL, srv.Client())

	if version != "unknown" {
		t.Errorf("expected unknown, got %s", version)
	}
}

func TestDiscoverVersion_Unreachable(t *testing.T) {
	// Use a URL that won't connect.
	version := DiscoverVersion("http://127.0.0.1:1", http.DefaultClient)

	if version != "unknown" {
		t.Errorf("expected unknown, got %s", version)
	}
}

func TestDiscoverVersionWithError_ReturnsError(t *testing.T) {
	version, err := DiscoverVersionWithError("http://127.0.0.1:1", http.DefaultClient)

	if version != "unknown" {
		t.Errorf("expected unknown, got %s", version)
	}
	if err == nil {
		t.Error("expected error, got nil")
	}
}

func TestDiscoverVersionWithError_Success(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte(`{"gitVersion": "v1.30.1"}`))
	}))
	defer srv.Close()

	version, err := DiscoverVersionWithError(srv.URL, srv.Client())

	if version != "v1.30.1" {
		t.Errorf("expected v1.30.1, got %s", version)
	}
	if err != nil {
		t.Errorf("expected no error, got %v", err)
	}
}
