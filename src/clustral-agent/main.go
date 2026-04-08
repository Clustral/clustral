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

	pb "clustral-agent/gen/clustral/v1"
	"clustral-agent/internal/auth"
	"clustral-agent/internal/config"
	"clustral-agent/internal/credential"
	"clustral-agent/internal/k8s"
	"clustral-agent/internal/proxy"
	"clustral-agent/internal/tunnel"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
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

	// Verify mTLS credentials exist.
	if !creds.HasMTLSCredentials() {
		logger.Error("No mTLS credentials found — AGENT_BOOTSTRAP_TOKEN is required for initial registration")
		os.Exit(1)
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

	// Load JWT for PerRPCCredentials.
	jwt, err := creds.ReadJWT()
	if err != nil {
		logger.Error("Failed to read JWT", "error", err)
		os.Exit(1)
	}
	jwtCreds := auth.NewJWTCredentials(jwt)

	// Start RenewalManager (cert + JWT auto-renewal).
	renewMgr := auth.NewRenewalManager(cfg, creds, jwtCreds, logger)
	go renewMgr.Run(ctx)

	// Start tunnel.
	mgr := tunnel.NewManager(cfg, creds, p, logger, k8sVersion, jwtCreds)
	mgr.Run(ctx)

	logger.Info("Clustral Agent stopped")
}

// registerAgent exchanges the bootstrap token for a client certificate + JWT
// via ClusterService.RegisterAgent.
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

// grpcTransportCreds returns TLS credentials for https:// URLs (InsecureSkipVerify
// for bootstrap only — after registration, the agent uses the CA cert for verification).
func grpcTransportCreds(rawURL string) grpc.DialOption {
	if strings.HasPrefix(rawURL, "https://") {
		return grpc.WithTransportCredentials(credentials.NewTLS(&tls.Config{
			InsecureSkipVerify: true,
		}))
	}
	return grpc.WithTransportCredentials(credentials.NewTLS(&tls.Config{}))
}

func stripScheme(url string) string {
	url = strings.TrimPrefix(url, "http://")
	url = strings.TrimPrefix(url, "https://")
	return url
}
