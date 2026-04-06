package main

import (
	"context"
	"fmt"
	"log/slog"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	pb "clustral-agent/gen/clustral/v1"
	"clustral-agent/internal/config"
	"clustral-agent/internal/credential"
	"clustral-agent/internal/proxy"
	"clustral-agent/internal/tunnel"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
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

	// Bootstrap credential if needed.
	if err := ensureCredential(cfg, creds, logger); err != nil {
		logger.Error("Failed to bootstrap credential", "error", err)
		os.Exit(1)
	}

	// Create k8s proxy.
	p := proxy.New(cfg.KubernetesAPIURL, cfg.KubernetesSkipTLSVerify, logger)

	// Run tunnel.
	ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer cancel()

	mgr := tunnel.NewManager(cfg, creds, p, logger)
	mgr.Run(ctx)

	logger.Info("Clustral Agent stopped")
}

func ensureCredential(cfg *config.Config, creds *credential.Store, logger *slog.Logger) error {
	// If bootstrap token provided, always issue fresh credential.
	if cfg.BootstrapToken != "" {
		logger.Info("Bootstrap token provided — issuing fresh credential")
		return issueCredential(cfg, creds, logger)
	}

	token, err := creds.ReadToken()
	if err != nil {
		return err
	}

	if token == "" {
		logger.Info("No credential found — need bootstrap token")
		return fmt.Errorf("AGENT_BOOTSTRAP_TOKEN is required when no credential file exists")
	}

	expiry, err := creds.ReadExpiry()
	if err != nil {
		return err
	}

	if !expiry.IsZero() && time.Until(expiry) < cfg.CredentialRotationThreshold {
		logger.Info("Credential expires soon — rotating")
		return rotateCredential(cfg, creds, token, logger)
	}

	logger.Info("Using existing credential", "path", cfg.CredentialPath)
	return nil
}

func issueCredential(cfg *config.Config, creds *credential.Store, logger *slog.Logger) error {
	conn, err := grpc.NewClient(stripScheme(cfg.ControlPlaneURL),
		grpc.WithTransportCredentials(insecure.NewCredentials()))
	if err != nil {
		return err
	}
	defer conn.Close()

	client := pb.NewAuthServiceClient(conn)

	resp, err := client.IssueAgentCredential(context.Background(), &pb.IssueAgentCredentialRequest{
		ClusterId:         cfg.ClusterID,
		BootstrapToken:    cfg.BootstrapToken,
		AgentPublicKeyPem: cfg.AgentPublicKeyPem,
	})
	if err != nil {
		return fmt.Errorf("IssueAgentCredential: %w", err)
	}

	expiresAt := resp.ExpiresAt.AsTime()
	if err := creds.Save(resp.Token, expiresAt); err != nil {
		return fmt.Errorf("save credential: %w", err)
	}

	logger.Info("Agent credential issued",
		"credentialId", resp.CredentialId,
		"expiresAt", expiresAt)
	return nil
}

func rotateCredential(cfg *config.Config, creds *credential.Store, currentToken string, logger *slog.Logger) error {
	conn, err := grpc.NewClient(stripScheme(cfg.ControlPlaneURL),
		grpc.WithTransportCredentials(insecure.NewCredentials()))
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

// Compatibility: handle ControlPlane URL with http:// prefix
// for gRPC which needs host:port without scheme.
func stripScheme(url string) string {
	url = strings.TrimPrefix(url, "http://")
	url = strings.TrimPrefix(url, "https://")
	return url
}
