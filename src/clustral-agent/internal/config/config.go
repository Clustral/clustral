package config

import (
	"os"
	"path/filepath"
	"strconv"
	"time"
)

type Config struct {
	ClusterID                  string
	ControlPlaneURL            string
	CredentialPath             string
	BootstrapToken             string
	AgentPublicKeyPem          string
	KubernetesAPIURL           string
	KubernetesSkipTLSVerify    bool
	HeartbeatInterval          time.Duration
	CredentialRotationThreshold time.Duration
	ReconnectInitialDelay      time.Duration
	ReconnectMaxDelay          time.Duration
	ReconnectBackoffMultiplier float64
	ReconnectMaxJitter         time.Duration
	AgentVersion               string
	CertRenewThreshold         time.Duration
	JWTRenewThreshold          time.Duration
	RenewalCheckInterval       time.Duration
}

func Load() *Config {
	return &Config{
		ClusterID:                  envStr("AGENT_CLUSTER_ID", ""),
		ControlPlaneURL:            envStr("AGENT_CONTROL_PLANE_URL", ""),
		CredentialPath:             envStr("AGENT_CREDENTIAL_PATH", defaultCredentialPath()),
		BootstrapToken:             envStr("AGENT_BOOTSTRAP_TOKEN", ""),
		AgentPublicKeyPem:          envStr("AGENT_PUBLIC_KEY_PEM", ""),
		KubernetesAPIURL:           envStr("AGENT_KUBERNETES_API_URL", "https://kubernetes.default.svc"),
		KubernetesSkipTLSVerify:    envBool("AGENT_KUBERNETES_SKIP_TLS_VERIFY", false),
		HeartbeatInterval:          envDuration("AGENT_HEARTBEAT_INTERVAL", 30*time.Second),
		CredentialRotationThreshold: envDuration("AGENT_CREDENTIAL_ROTATION_THRESHOLD", 30*24*time.Hour),
		ReconnectInitialDelay:      envDuration("AGENT_RECONNECT_INITIAL_DELAY", 2*time.Second),
		ReconnectMaxDelay:          envDuration("AGENT_RECONNECT_MAX_DELAY", 60*time.Second),
		ReconnectBackoffMultiplier: envFloat("AGENT_RECONNECT_BACKOFF_MULTIPLIER", 2.0),
		ReconnectMaxJitter:         envDuration("AGENT_RECONNECT_MAX_JITTER", 5*time.Second),
		AgentVersion:               envStr("AGENT_VERSION", "0.1.0"),
		CertRenewThreshold:        envDuration("AGENT_CERT_RENEW_THRESHOLD", 30*24*time.Hour),
		JWTRenewThreshold:         envDuration("AGENT_JWT_RENEW_THRESHOLD", 7*24*time.Hour),
		RenewalCheckInterval:      envDuration("AGENT_RENEWAL_CHECK_INTERVAL", 6*time.Hour),
	}
}

func defaultCredentialPath() string {
	home, err := os.UserHomeDir()
	if err != nil {
		return "/etc/clustral/agent.token"
	}
	return filepath.Join(home, ".clustral", "agent.token")
}

func envStr(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}

func envBool(key string, def bool) bool {
	if v := os.Getenv(key); v != "" {
		b, err := strconv.ParseBool(v)
		if err == nil {
			return b
		}
	}
	return def
}

func envDuration(key string, def time.Duration) time.Duration {
	if v := os.Getenv(key); v != "" {
		d, err := time.ParseDuration(v)
		if err == nil {
			return d
		}
	}
	return def
}

func envFloat(key string, def float64) float64 {
	if v := os.Getenv(key); v != "" {
		f, err := strconv.ParseFloat(v, 64)
		if err == nil {
			return f
		}
	}
	return def
}
