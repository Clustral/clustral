package config

import (
	"testing"
	"time"
)

func TestEnvStr_Set(t *testing.T) {
	t.Setenv("TEST_STR", "hello")
	if got := envStr("TEST_STR", "default"); got != "hello" {
		t.Errorf("envStr = %q, want %q", got, "hello")
	}
}

func TestEnvStr_Unset(t *testing.T) {
	if got := envStr("TEST_STR_MISSING", "fallback"); got != "fallback" {
		t.Errorf("envStr = %q, want %q", got, "fallback")
	}
}

func TestEnvStr_Empty(t *testing.T) {
	t.Setenv("TEST_STR_EMPTY", "")
	if got := envStr("TEST_STR_EMPTY", "default"); got != "default" {
		t.Errorf("envStr empty should return default, got %q", got)
	}
}

func TestEnvBool(t *testing.T) {
	tests := []struct {
		value string
		want  bool
	}{
		{"true", true},
		{"false", false},
		{"1", true},
		{"0", false},
		{"TRUE", true},
		{"FALSE", false},
	}

	for _, tc := range tests {
		t.Run(tc.value, func(t *testing.T) {
			t.Setenv("TEST_BOOL", tc.value)
			if got := envBool("TEST_BOOL", !tc.want); got != tc.want {
				t.Errorf("envBool(%q) = %v, want %v", tc.value, got, tc.want)
			}
		})
	}
}

func TestEnvBool_Invalid(t *testing.T) {
	t.Setenv("TEST_BOOL_BAD", "maybe")
	if got := envBool("TEST_BOOL_BAD", true); got != true {
		t.Errorf("envBool invalid should return default true, got %v", got)
	}
}

func TestEnvBool_Unset(t *testing.T) {
	if got := envBool("TEST_BOOL_MISSING", false); got != false {
		t.Errorf("envBool unset should return default false, got %v", got)
	}
}

func TestEnvDuration(t *testing.T) {
	tests := []struct {
		value string
		want  time.Duration
	}{
		{"30s", 30 * time.Second},
		{"2h", 2 * time.Hour},
		{"5m30s", 5*time.Minute + 30*time.Second},
		{"100ms", 100 * time.Millisecond},
	}

	for _, tc := range tests {
		t.Run(tc.value, func(t *testing.T) {
			t.Setenv("TEST_DUR", tc.value)
			if got := envDuration("TEST_DUR", 0); got != tc.want {
				t.Errorf("envDuration(%q) = %v, want %v", tc.value, got, tc.want)
			}
		})
	}
}

func TestEnvDuration_Invalid(t *testing.T) {
	t.Setenv("TEST_DUR_BAD", "notaduration")
	def := 10 * time.Second
	if got := envDuration("TEST_DUR_BAD", def); got != def {
		t.Errorf("envDuration invalid should return default %v, got %v", def, got)
	}
}

func TestEnvFloat(t *testing.T) {
	tests := []struct {
		value string
		want  float64
	}{
		{"2.0", 2.0},
		{"1.5", 1.5},
		{"0.1", 0.1},
	}

	for _, tc := range tests {
		t.Run(tc.value, func(t *testing.T) {
			t.Setenv("TEST_FLOAT", tc.value)
			if got := envFloat("TEST_FLOAT", 0); got != tc.want {
				t.Errorf("envFloat(%q) = %v, want %v", tc.value, got, tc.want)
			}
		})
	}
}

func TestEnvFloat_Invalid(t *testing.T) {
	t.Setenv("TEST_FLOAT_BAD", "notafloat")
	if got := envFloat("TEST_FLOAT_BAD", 3.14); got != 3.14 {
		t.Errorf("envFloat invalid should return default 3.14, got %v", got)
	}
}

func TestLoad_Defaults(t *testing.T) {
	cfg := Load()

	if cfg.KubernetesAPIURL != "https://kubernetes.default.svc" {
		t.Errorf("KubernetesAPIURL = %q, want default", cfg.KubernetesAPIURL)
	}
	if cfg.HeartbeatInterval != 30*time.Second {
		t.Errorf("HeartbeatInterval = %v, want 30s", cfg.HeartbeatInterval)
	}
	if cfg.ReconnectBackoffMultiplier != 2.0 {
		t.Errorf("ReconnectBackoffMultiplier = %v, want 2.0", cfg.ReconnectBackoffMultiplier)
	}
	if cfg.AgentVersion != "0.1.0" {
		t.Errorf("AgentVersion = %q, want 0.1.0", cfg.AgentVersion)
	}
	if cfg.KubernetesSkipTLSVerify {
		t.Error("KubernetesSkipTLSVerify should default to false")
	}
}

func TestLoad_WithEnvVars(t *testing.T) {
	t.Setenv("AGENT_CLUSTER_ID", "test-cluster")
	t.Setenv("AGENT_CONTROL_PLANE_URL", "http://localhost:5001")
	t.Setenv("AGENT_KUBERNETES_SKIP_TLS_VERIFY", "true")
	t.Setenv("AGENT_HEARTBEAT_INTERVAL", "10s")
	t.Setenv("AGENT_RECONNECT_BACKOFF_MULTIPLIER", "3.0")

	cfg := Load()

	if cfg.ClusterID != "test-cluster" {
		t.Errorf("ClusterID = %q, want test-cluster", cfg.ClusterID)
	}
	if cfg.ControlPlaneURL != "http://localhost:5001" {
		t.Errorf("ControlPlaneURL = %q", cfg.ControlPlaneURL)
	}
	if !cfg.KubernetesSkipTLSVerify {
		t.Error("KubernetesSkipTLSVerify should be true")
	}
	if cfg.HeartbeatInterval != 10*time.Second {
		t.Errorf("HeartbeatInterval = %v, want 10s", cfg.HeartbeatInterval)
	}
	if cfg.ReconnectBackoffMultiplier != 3.0 {
		t.Errorf("ReconnectBackoffMultiplier = %v, want 3.0", cfg.ReconnectBackoffMultiplier)
	}
}
