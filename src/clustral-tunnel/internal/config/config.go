package config

import (
	"fmt"
	"os"
	"strconv"
	"time"
)

// Config holds all configuration for the tunnel service.
type Config struct {
	// Agent-facing gRPC port (mTLS).
	AgentPort string

	// Internal gRPC port (no auth, pod-local only).
	InternalPort string

	// Health HTTP port.
	HealthPort string

	// MongoDB connection string.
	MongoURI string

	// MongoDB database name.
	MongoDatabase string

	// Redis URL.
	RedisURL string

	// CA certificate and key paths for mTLS + cert issuance.
	CACertPath string
	CAKeyPath  string

	// Session TTL in Redis.
	SessionTTL time.Duration

	// Pod name for Redis session ownership.
	PodName string

	// JWT validity in days.
	JWTValidityDays int

	// Certificate validity in days.
	CertValidityDays int
}

// Load reads configuration from environment variables.
func Load() (*Config, error) {
	cfg := &Config{
		AgentPort:        envOrDefault("TUNNEL_AGENT_PORT", "5443"),
		InternalPort:     envOrDefault("TUNNEL_INTERNAL_PORT", "50051"),
		HealthPort:       envOrDefault("TUNNEL_HEALTH_PORT", "8081"),
		MongoURI:         envOrDefault("TUNNEL_MONGO_URI", "mongodb://localhost:27017"),
		MongoDatabase:    envOrDefault("TUNNEL_MONGO_DATABASE", "clustral"),
		RedisURL:         envOrDefault("TUNNEL_REDIS_URL", "localhost:6379"),
		CACertPath:       os.Getenv("TUNNEL_CA_CERT_PATH"),
		CAKeyPath:        os.Getenv("TUNNEL_CA_KEY_PATH"),
		SessionTTL:       parseDuration("TUNNEL_SESSION_TTL", 5*time.Minute),
		PodName:          envOrDefault("TUNNEL_POD_NAME", "tunnel-0"),
		JWTValidityDays:  parseInt("TUNNEL_JWT_VALIDITY_DAYS", 90),
		CertValidityDays: parseInt("TUNNEL_CERT_VALIDITY_DAYS", 365),
	}

	if cfg.CACertPath == "" {
		return nil, fmt.Errorf("TUNNEL_CA_CERT_PATH is required")
	}
	if cfg.CAKeyPath == "" {
		return nil, fmt.Errorf("TUNNEL_CA_KEY_PATH is required")
	}

	return cfg, nil
}

func envOrDefault(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}

func parseDuration(key string, def time.Duration) time.Duration {
	if v := os.Getenv(key); v != "" {
		d, err := time.ParseDuration(v)
		if err == nil {
			return d
		}
	}
	return def
}

func parseInt(key string, def int) int {
	if v := os.Getenv(key); v != "" {
		n, err := strconv.Atoi(v)
		if err == nil {
			return n
		}
	}
	return def
}
