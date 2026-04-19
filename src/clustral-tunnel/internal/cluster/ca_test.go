package cluster

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"math/big"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func generateTestCA(t *testing.T) *CA {
	t.Helper()

	caKey, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	require.NoError(t, err)

	template := &x509.Certificate{
		SerialNumber:          big.NewInt(1),
		Subject:               pkix.Name{CommonName: "Test CA", Organization: []string{"clustral-test"}},
		NotBefore:             time.Now().Add(-1 * time.Hour),
		NotAfter:              time.Now().Add(24 * time.Hour),
		IsCA:                  true,
		KeyUsage:              x509.KeyUsageCertSign | x509.KeyUsageCRLSign,
		BasicConstraintsValid: true,
	}

	caCertDER, err := x509.CreateCertificate(rand.Reader, template, template, &caKey.PublicKey, caKey)
	require.NoError(t, err)

	certPEM := pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: caCertDER})
	keyDER, err := x509.MarshalECPrivateKey(caKey)
	require.NoError(t, err)
	keyPEM := pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: keyDER})

	ca, err := LoadCAFromPEM(certPEM, keyPEM)
	require.NoError(t, err)
	return ca
}

func TestCA_IssueCertificate(t *testing.T) {
	ca := generateTestCA(t)

	certPEM, keyPEM, err := ca.IssueCertificate("test-agent", 365)
	require.NoError(t, err)
	assert.NotEmpty(t, certPEM)
	assert.NotEmpty(t, keyPEM)

	// Parse the issued certificate.
	block, _ := pem.Decode(certPEM)
	require.NotNil(t, block)

	cert, err := x509.ParseCertificate(block.Bytes)
	require.NoError(t, err)

	assert.Equal(t, "test-agent", cert.Subject.CommonName)
	assert.Contains(t, cert.Subject.Organization, "clustral")
	assert.True(t, cert.NotAfter.After(time.Now()))
	assert.True(t, cert.NotBefore.Before(time.Now()))
}

func TestCA_IssuedCertVerifiesAgainstCA(t *testing.T) {
	ca := generateTestCA(t)

	certPEM, _, err := ca.IssueCertificate("test-agent", 365)
	require.NoError(t, err)

	block, _ := pem.Decode(certPEM)
	cert, err := x509.ParseCertificate(block.Bytes)
	require.NoError(t, err)

	// Verify cert against CA.
	pool := x509.NewCertPool()
	pool.AddCert(ca.Certificate())

	chains, err := cert.Verify(x509.VerifyOptions{
		Roots:     pool,
		KeyUsages: []x509.ExtKeyUsage{x509.ExtKeyUsageClientAuth},
	})
	require.NoError(t, err)
	assert.NotEmpty(t, chains)
}

func TestCA_GetCACertificatePEM(t *testing.T) {
	ca := generateTestCA(t)
	pemStr := ca.GetCACertificatePEM()
	assert.Contains(t, pemStr, "BEGIN CERTIFICATE")
}
