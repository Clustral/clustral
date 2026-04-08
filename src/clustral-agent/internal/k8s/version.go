package k8s

import (
	"encoding/json"
	"fmt"
	"net/http"
	"time"
)

// versionResponse mirrors the k8s /version endpoint response.
// Only the fields we need are included.
type versionResponse struct {
	GitVersion string `json:"gitVersion"`
}

// DiscoverVersion calls GET /version on the k8s API server and returns
// the gitVersion string (e.g., "v1.29.0"). Returns "unknown" on any error
// so the agent can still connect even if version discovery fails.
func DiscoverVersion(apiURL string, httpClient *http.Client) string {
	client := *httpClient
	client.Timeout = 5 * time.Second

	resp, err := client.Get(apiURL + "/version")
	if err != nil {
		return "unknown"
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return "unknown"
	}

	var v versionResponse
	if err := json.NewDecoder(resp.Body).Decode(&v); err != nil {
		return "unknown"
	}

	if v.GitVersion == "" {
		return "unknown"
	}

	return v.GitVersion
}

// DiscoverVersionWithError is like DiscoverVersion but returns the error
// for logging purposes.
func DiscoverVersionWithError(apiURL string, httpClient *http.Client) (string, error) {
	client := *httpClient
	client.Timeout = 5 * time.Second

	resp, err := client.Get(apiURL + "/version")
	if err != nil {
		return "unknown", fmt.Errorf("GET /version: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return "unknown", fmt.Errorf("GET /version returned %d", resp.StatusCode)
	}

	var v versionResponse
	if err := json.NewDecoder(resp.Body).Decode(&v); err != nil {
		return "unknown", fmt.Errorf("parse /version: %w", err)
	}

	if v.GitVersion == "" {
		return "unknown", fmt.Errorf("empty gitVersion in /version response")
	}

	return v.GitVersion, nil
}
