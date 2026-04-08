package auth

import (
	"context"
	"crypto/tls"
	"crypto/x509"
	"encoding/json"
	"encoding/base64"
	"fmt"
	"log/slog"
	"strings"
	"time"

	pb "clustral-agent/gen/clustral/v1"
	"clustral-agent/internal/config"
	"clustral-agent/internal/credential"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
)

// RenewalManager periodically checks certificate and JWT expiry and
// renews them before they expire.
type RenewalManager struct {
	cfg      *config.Config
	creds    *credential.Store
	jwtCreds *JWTCredentials
	logger   *slog.Logger
}

// NewRenewalManager creates a new RenewalManager.
func NewRenewalManager(cfg *config.Config, creds *credential.Store, jwtCreds *JWTCredentials, logger *slog.Logger) *RenewalManager {
	return &RenewalManager{
		cfg:      cfg,
		creds:    creds,
		jwtCreds: jwtCreds,
		logger:   logger,
	}
}

// Run starts the periodic renewal loop. Blocks until ctx is cancelled.
func (rm *RenewalManager) Run(ctx context.Context) {
	ticker := time.NewTicker(rm.cfg.RenewalCheckInterval)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			rm.logger.Info("RenewalManager shutting down")
			return
		case <-ticker.C:
			rm.checkAndRenew(ctx)
		}
	}
}

// TriggerJWTRenewal forces an immediate JWT renewal (called on Unauthenticated error).
func (rm *RenewalManager) TriggerJWTRenewal(ctx context.Context) error {
	rm.logger.Info("Triggering immediate JWT renewal")
	return rm.renewJWT(ctx)
}

func (rm *RenewalManager) checkAndRenew(ctx context.Context) {
	// Check certificate expiry
	cert, err := rm.creds.ReadCert()
	if err != nil {
		rm.logger.Warn("Could not read certificate for renewal check", "error", err)
	} else if len(cert.Certificate) > 0 {
		parsed, err := x509.ParseCertificate(cert.Certificate[0])
		if err == nil {
			remaining := time.Until(parsed.NotAfter)
			if remaining < rm.cfg.CertRenewThreshold {
				rm.logger.Info("Certificate expires soon, renewing",
					"expiresIn", remaining.Round(time.Hour),
					"threshold", rm.cfg.CertRenewThreshold)
				if err := rm.renewCert(ctx); err != nil {
					rm.logger.Error("Certificate renewal failed", "error", err)
				}
			}
		}
	}

	// Check JWT expiry
	jwtExpiry, err := parseJWTExpiry(rm.jwtCreds.Token())
	if err != nil {
		rm.logger.Warn("Could not parse JWT expiry", "error", err)
	} else {
		remaining := time.Until(jwtExpiry)
		if remaining < rm.cfg.JWTRenewThreshold {
			rm.logger.Info("JWT expires soon, renewing",
				"expiresIn", remaining.Round(time.Hour),
				"threshold", rm.cfg.JWTRenewThreshold)
			if err := rm.renewJWT(ctx); err != nil {
				rm.logger.Error("JWT renewal failed", "error", err)
			}
		}
	}
}

func (rm *RenewalManager) renewCert(ctx context.Context) error {
	conn, err := rm.dialMTLS()
	if err != nil {
		return fmt.Errorf("dial: %w", err)
	}
	defer conn.Close()

	client := pb.NewClusterServiceClient(conn)
	resp, err := client.RenewCertificate(ctx, &pb.RenewCertificateRequest{
		ClusterId: rm.cfg.ClusterID,
	})
	if err != nil {
		return fmt.Errorf("RenewCertificate RPC: %w", err)
	}

	if err := rm.creds.SaveCertAndKey([]byte(resp.ClientCertificatePem), []byte(resp.ClientPrivateKeyPem)); err != nil {
		return fmt.Errorf("save cert: %w", err)
	}

	rm.logger.Info("Certificate renewed successfully",
		"expiresAt", resp.ExpiresAt.AsTime())
	return nil
}

func (rm *RenewalManager) renewJWT(ctx context.Context) error {
	conn, err := rm.dialMTLS()
	if err != nil {
		return fmt.Errorf("dial: %w", err)
	}
	defer conn.Close()

	client := pb.NewClusterServiceClient(conn)
	resp, err := client.RenewToken(ctx, &pb.RenewTokenRequest{
		ClusterId: rm.cfg.ClusterID,
	})
	if err != nil {
		return fmt.Errorf("RenewToken RPC: %w", err)
	}

	if err := rm.creds.SaveJWT(resp.Jwt); err != nil {
		return fmt.Errorf("save JWT: %w", err)
	}

	rm.jwtCreds.Update(resp.Jwt)
	rm.logger.Info("JWT renewed successfully",
		"expiresAt", resp.ExpiresAt.AsTime())
	return nil
}

func (rm *RenewalManager) dialMTLS() (*grpc.ClientConn, error) {
	cert, err := rm.creds.ReadCert()
	if err != nil {
		return nil, fmt.Errorf("read cert: %w", err)
	}
	caPool, err := rm.creds.ReadCACert()
	if err != nil {
		return nil, fmt.Errorf("read CA cert: %w", err)
	}

	tlsConfig := &tls.Config{
		Certificates: []tls.Certificate{cert},
		RootCAs:      caPool,
	}

	addr := strings.TrimPrefix(strings.TrimPrefix(rm.cfg.ControlPlaneURL, "https://"), "http://")
	return grpc.NewClient(addr,
		grpc.WithTransportCredentials(credentials.NewTLS(tlsConfig)),
		grpc.WithPerRPCCredentials(rm.jwtCreds),
	)
}

// parseJWTExpiry decodes the JWT payload (without verification) to read the exp claim.
func parseJWTExpiry(jwt string) (time.Time, error) {
	parts := strings.Split(jwt, ".")
	if len(parts) != 3 {
		return time.Time{}, fmt.Errorf("invalid JWT format")
	}

	payload, err := base64.RawURLEncoding.DecodeString(parts[1])
	if err != nil {
		return time.Time{}, fmt.Errorf("decode payload: %w", err)
	}

	var claims struct {
		Exp int64 `json:"exp"`
	}
	if err := json.Unmarshal(payload, &claims); err != nil {
		return time.Time{}, fmt.Errorf("unmarshal claims: %w", err)
	}

	if claims.Exp == 0 {
		return time.Time{}, fmt.Errorf("no exp claim")
	}

	return time.Unix(claims.Exp, 0), nil
}
