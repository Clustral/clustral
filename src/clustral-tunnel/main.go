package main

import (
	"context"
	"crypto/tls"
	"crypto/x509"
	"log/slog"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"

	pb "clustral-tunnel/gen/clustral/v1"
	internalpb "clustral-tunnel/gen/tunnelproxy/v1"
	"clustral-tunnel/internal/auth"
	"clustral-tunnel/internal/cluster"
	"clustral-tunnel/internal/config"
	"clustral-tunnel/internal/proxy"
	tunnelredis "clustral-tunnel/internal/redis"
	"clustral-tunnel/internal/tunnel"

	"go.mongodb.org/mongo-driver/v2/mongo"
	"go.mongodb.org/mongo-driver/v2/mongo/options"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
)

var version = "0.0.0-dev"

func main() {
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelInfo}))

	cfg, err := config.Load()
	if err != nil {
		logger.Error("Failed to load config", "error", err)
		os.Exit(1)
	}

	logger.Info("Clustral Tunnel Service starting",
		"agentPort", cfg.AgentPort,
		"internalPort", cfg.InternalPort,
		"healthPort", cfg.HealthPort,
		"version", version)

	// Load CA.
	ca, err := cluster.LoadCA(cfg.CACertPath, cfg.CAKeyPath)
	if err != nil {
		logger.Error("Failed to load CA", "error", err)
		os.Exit(1)
	}

	// Connect to Redis.
	registry, err := tunnelredis.NewSessionRegistry(cfg.RedisURL, cfg.SessionTTL)
	if err != nil {
		logger.Error("Failed to connect to Redis", "error", err)
		os.Exit(1)
	}

	// Connect to MongoDB.
	mongoClient, err := mongo.Connect(options.Client().ApplyURI(cfg.MongoURI))
	if err != nil {
		logger.Error("Failed to connect to MongoDB", "error", err)
		os.Exit(1)
	}
	mongoDB := mongoClient.Database(cfg.MongoDatabase)

	// Create JWT signer — uses the CA's RSA private key.
	jwtSigner, err := cluster.NewJWTSignerFromCA(ca, cfg.JWTValidityDays)
	if err != nil {
		logger.Error("Failed to create JWT signer", "error", err)
		os.Exit(1)
	}

	// Create JWT validator from the signing key's public key.
	jwtValidator := auth.NewJWTValidatorFromKey(jwtSigner.PublicKey())

	// Create session manager.
	sessionMgr := tunnel.NewSessionManager(registry, cfg.PodName)

	// Create cluster service (also provides token version lookup).
	clusterSvc := cluster.NewClusterServiceServer(mongoDB, ca, jwtSigner, sessionMgr, logger)

	// Auth interceptor.
	authInterceptor := auth.NewAuthInterceptor(jwtValidator, clusterSvc.GetTokenVersion, logger)

	// --- Agent-facing gRPC server (mTLS) ---
	caCertPool := x509.NewCertPool()
	caCertPool.AddCert(ca.Certificate())

	tlsConfig := &tls.Config{
		ClientAuth: tls.RequireAnyClientCert,
		ClientCAs:  caCertPool,
		MinVersion: tls.VersionTLS12,
	}

	agentLis, err := net.Listen("tcp", ":"+cfg.AgentPort)
	if err != nil {
		logger.Error("Failed to listen on agent port", "port", cfg.AgentPort, "error", err)
		os.Exit(1)
	}

	agentServer := grpc.NewServer(
		grpc.Creds(credentials.NewTLS(tlsConfig)),
		grpc.UnaryInterceptor(authInterceptor.UnaryInterceptor()),
		grpc.StreamInterceptor(authInterceptor.StreamInterceptor()),
	)

	tunnelSvc := tunnel.NewTunnelServiceServer(sessionMgr, mongoDB, logger)
	pb.RegisterTunnelServiceServer(agentServer, tunnelSvc)
	pb.RegisterClusterServiceServer(agentServer, clusterSvc)

	// --- Internal gRPC server (no auth) ---
	internalLis, err := net.Listen("tcp", ":"+cfg.InternalPort)
	if err != nil {
		logger.Error("Failed to listen on internal port", "port", cfg.InternalPort, "error", err)
		os.Exit(1)
	}

	internalServer := grpc.NewServer()
	proxySvc := proxy.NewTunnelProxyServer(sessionMgr)
	internalpb.RegisterTunnelProxyServer(internalServer, proxySvc)

	// --- Health HTTP server ---
	healthMux := http.NewServeMux()
	healthMux.HandleFunc("/healthz", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte("ok"))
	})

	healthServer := &http.Server{
		Addr:    ":" + cfg.HealthPort,
		Handler: healthMux,
	}

	// Start servers.
	go func() {
		logger.Info("Agent gRPC server listening", "port", cfg.AgentPort)
		if err := agentServer.Serve(agentLis); err != nil {
			logger.Error("Agent gRPC server error", "error", err)
		}
	}()

	go func() {
		logger.Info("Internal gRPC server listening", "port", cfg.InternalPort)
		if err := internalServer.Serve(internalLis); err != nil {
			logger.Error("Internal gRPC server error", "error", err)
		}
	}()

	go func() {
		logger.Info("Health server listening", "port", cfg.HealthPort)
		if err := healthServer.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			logger.Error("Health server error", "error", err)
		}
	}()

	// Block on SIGTERM/SIGINT.
	ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer cancel()
	<-ctx.Done()

	logger.Info("Shutting down gracefully...")
	agentServer.GracefulStop()
	internalServer.GracefulStop()
	_ = healthServer.Shutdown(context.Background())

	logger.Info("Clustral Tunnel Service stopped")
}
