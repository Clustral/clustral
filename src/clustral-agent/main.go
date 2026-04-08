package main

import (
	"context"
	"crypto/tls"
	"fmt"
	"log/slog"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	pb "clustral-agent/gen/clustral/v1"
	"clustral-agent/internal/auth"
	"clustral-agent/internal/config"
	"clustral-agent/internal/credential"
	"clustral-agent/internal/k8s"
	"clustral-agent/internal/proxy"
	"clustral-agent/internal/tunnel"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	grpcinsecure "google.golang.org/grpc/credentials/insecure"
)

func main() {
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelInfo}))

	cfg := config.Load()

	if cfg.ClusterID == "" || cfg.ControlPlaneURL == "" {
		logger.Error("AGENT_CLUSTER_ID and AGENT_CONTROL_PLANE_URL are required")
		os.Exit(1)
	}

	logger.Info("Clustral Agent starting",
		"clusterId", cfg.ClusterID,
		"controlPlane", cfg.ControlPlaneURL)

	creds := credential.NewStore(cfg.CredentialPath)

	// Bootstrap: exchange bootstrap token for mTLS cert + JWT.
	if cfg.BootstrapToken != "" {
		logger.Info("Bootstrap token provided — registering agent with mTLS + JWT")
		if err := registerAgent(cfg, creds, logger); err != nil {
			logger.Error("Failed to register agent", "error", err)
			os.Exit(1)
		}
	}

	// Check for mTLS credentials.
	if !creds.HasMTLSCredentials() {
		// Fall back to legacy bearer token flow.
		logger.Info("No mTLS credentials found — using legacy bearer token auth")
		if err := ensureLegacyCredential(cfg, creds, logger); err != nil {
			logger.Error("Failed to bootstrap credential", "error", err)
			os.Exit(1)
		}
	}

	// Create k8s proxy.
	p := proxy.New(cfg.KubernetesAPIURL, cfg.KubernetesSkipTLSVerify, logger)

	// Discover Kubernetes version (non-fatal).
	k8sVersion, err := k8s.DiscoverVersionWithError(cfg.KubernetesAPIURL, p.HTTPClient())
	if err != nil {
		logger.Warn("Could not discover Kubernetes version", "error", err)
	} else {
		logger.Info("Discovered Kubernetes version", "version", k8sVersion)
	}

	ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer cancel()

	// Start RenewalManager if mTLS credentials exist.
	if creds.HasMTLSCredentials() {
		jwt, err := creds.ReadJWT()
		if err != nil {
			logger.Error("Failed to read JWT", "error", err)
			os.Exit(1)
		}
		jwtCreds := auth.NewJWTCredentials(jwt)

		renewMgr := auth.NewRenewalManager(cfg, creds, jwtCreds, logger)
		go renewMgr.Run(ctx)

		mgr := tunnel.NewManager(cfg, creds, p, logger, k8sVersion)
		mgr.SetMTLSCredentials(jwtCreds)
		mgr.Run(ctx)
	} else {
		// Legacy bearer token tunnel.
		mgr := tunnel.NewManager(cfg, creds, p, logger, k8sVersion)
		mgr.Run(ctx)
	}

	logger.Info("Clustral Agent stopped")
}

// registerAgent exchanges the bootstrap token for a client certificate + JWT
// via ClusterService.RegisterAgent. This is the new mTLS bootstrap flow.
func registerAgent(cfg *config.Config, creds *credential.Store, logger *slog.Logger) error {
	conn, err := grpc.NewClient(stripScheme(cfg.ControlPlaneURL),
		grpcTransportCreds(cfg.ControlPlaneURL))
	if err != nil {
		return err
	}
	defer conn.Close()

	client := pb.NewClusterServiceClient(conn)

	resp, err := client.RegisterAgent(context.Background(), &pb.RegisterAgentRequest{
		ClusterId:      cfg.ClusterID,
		BootstrapToken: cfg.BootstrapToken,
	})
	if err != nil {
		return fmt.Errorf("RegisterAgent: %w", err)
	}

	// Save all credentials to disk.
	if err := creds.SaveCertAndKey([]byte(resp.ClientCertificatePem), []byte(resp.ClientPrivateKeyPem)); err != nil {
		return fmt.Errorf("save cert: %w", err)
	}
	if err := creds.SaveCACert([]byte(resp.CaCertificatePem)); err != nil {
		return fmt.Errorf("save CA cert: %w", err)
	}
	if err := creds.SaveJWT(resp.Jwt); err != nil {
		return fmt.Errorf("save JWT: %w", err)
	}

	logger.Info("Agent registered with mTLS + JWT",
		"clusterId", resp.ClusterId,
		"certExpiresAt", resp.CertExpiresAt.AsTime(),
		"jwtExpiresAt", resp.JwtExpiresAt.AsTime())
	return nil
}

// ensureLegacyCredential handles the old bearer token bootstrap (backward compat).
func ensureLegacyCredential(cfg *config.Config, creds *credential.Store, logger *slog.Logger) error {
	token, err := creds.ReadToken()
	if err != nil {
		return err
	}

	if token == "" {
		return fmt.Errorf("no credentials found — AGENT_BOOTSTRAP_TOKEN is required for initial registration")
	}

	expiry, err := creds.ReadExpiry()
	if err != nil {
		return err
	}

	if !expiry.IsZero() && time.Until(expiry) < cfg.CredentialRotationThreshold {
		logger.Info("Credential expires soon — rotating")
		return rotateLegacyCredential(cfg, creds, token, logger)
	}

	logger.Info("Using existing legacy credential", "path", cfg.CredentialPath)
	return nil
}

func rotateLegacyCredential(cfg *config.Config, creds *credential.Store, currentToken string, logger *slog.Logger) error {
	conn, err := grpc.NewClient(stripScheme(cfg.ControlPlaneURL),
		grpcTransportCreds(cfg.ControlPlaneURL))
	if err != nil {
		return err
	}
	defer conn.Close()

	client := pb.NewAuthServiceClient(conn)

	resp, err := client.RotateAgentCredential(context.Background(), &pb.RotateAgentCredentialRequest{
		ClusterId:    cfg.ClusterID,
		CurrentToken: currentToken,
	})
	if err != nil {
		return fmt.Errorf("RotateAgentCredential: %w", err)
	}

	expiresAt := resp.ExpiresAt.AsTime()
	if err := creds.Save(resp.Token, expiresAt); err != nil {
		return fmt.Errorf("save credential: %w", err)
	}

	logger.Info("Agent credential rotated",
		"credentialId", resp.CredentialId,
		"expiresAt", expiresAt)
	return nil
}

// grpcTransportCreds returns TLS credentials for https:// URLs,
// or insecure credentials for http:// (local dev).
func grpcTransportCreds(rawURL string) grpc.DialOption {
	if strings.HasPrefix(rawURL, "https://") {
		return grpc.WithTransportCredentials(credentials.NewTLS(&tls.Config{
			InsecureSkipVerify: true, // used only for initial bootstrap; mTLS uses proper CA after
		}))
	}
	return grpc.WithTransportCredentials(grpcinsecure.NewCredentials())
}

// stripScheme removes http:// or https:// prefix so grpc.NewClient
// receives a host:port target.
func stripScheme(url string) string {
	url = strings.TrimPrefix(url, "http://")
	url = strings.TrimPrefix(url, "https://")
	return url
}
