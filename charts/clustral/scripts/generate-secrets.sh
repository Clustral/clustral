#!/usr/bin/env bash
set -euo pipefail

# Generate Clustral platform secrets for manual (non-cert-manager) installs.
# Usage: ./generate-secrets.sh [--namespace <ns>] [--domain <domain>]

NAMESPACE="default"
DOMAIN="clustral.example.com"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --namespace) NAMESPACE="$2"; shift 2 ;;
    --domain)    DOMAIN="$2";    shift 2 ;;
    *)           echo "Unknown option: $1"; exit 1 ;;
  esac
done

TMPDIR=$(mktemp -d)
trap 'rm -rf "$TMPDIR"' EXIT

echo "Generating secrets in namespace '$NAMESPACE' for domain '$DOMAIN'..."
echo ""

# ── 1. Internal JWT (ES256 keypair) ─────────────────────────────────────────
echo "1/4  Internal JWT keypair (ES256)..."
openssl ecparam -genkey -name prime256v1 -noout -out "$TMPDIR/internal-jwt-private.pem" 2>/dev/null
openssl ec -in "$TMPDIR/internal-jwt-private.pem" -pubout -out "$TMPDIR/internal-jwt-public.pem" 2>/dev/null

kubectl create secret generic clustral-internal-jwt \
  --namespace="$NAMESPACE" \
  --from-file=private.pem="$TMPDIR/internal-jwt-private.pem" \
  --from-file=public.pem="$TMPDIR/internal-jwt-public.pem" \
  --dry-run=client -o yaml | kubectl apply -f -

# ── 2. Kubeconfig JWT (ES256 keypair) ───────────────────────────────────────
echo "2/4  Kubeconfig JWT keypair (ES256)..."
openssl ecparam -genkey -name prime256v1 -noout -out "$TMPDIR/kubeconfig-jwt-private.pem" 2>/dev/null
openssl ec -in "$TMPDIR/kubeconfig-jwt-private.pem" -pubout -out "$TMPDIR/kubeconfig-jwt-public.pem" 2>/dev/null

kubectl create secret generic clustral-kubeconfig-jwt \
  --namespace="$NAMESPACE" \
  --from-file=private.pem="$TMPDIR/kubeconfig-jwt-private.pem" \
  --from-file=public.pem="$TMPDIR/kubeconfig-jwt-public.pem" \
  --dry-run=client -o yaml | kubectl apply -f -

# ── 3. CA (self-signed, EC P-256, 10 year validity) ─────────────────────────
echo "3/4  Self-signed CA certificate (EC P-256, 10y)..."
openssl ecparam -genkey -name prime256v1 -noout -out "$TMPDIR/ca.key" 2>/dev/null
openssl req -new -x509 -key "$TMPDIR/ca.key" \
  -out "$TMPDIR/ca.crt" \
  -days 3650 \
  -subj "/CN=clustral-ca" \
  2>/dev/null

kubectl create secret generic clustral-ca \
  --namespace="$NAMESPACE" \
  --from-file=ca.crt="$TMPDIR/ca.crt" \
  --from-file=ca.key="$TMPDIR/ca.key" \
  --dry-run=client -o yaml | kubectl apply -f -

# ── 4. TLS certificate (signed by CA, EC P-256, 1 year) ─────────────────────
echo "4/4  TLS certificate for $DOMAIN (EC P-256, 1y, signed by CA)..."
openssl ecparam -genkey -name prime256v1 -noout -out "$TMPDIR/tls.key" 2>/dev/null
openssl req -new -key "$TMPDIR/tls.key" \
  -out "$TMPDIR/tls.csr" \
  -subj "/CN=$DOMAIN" \
  2>/dev/null

cat > "$TMPDIR/tls-ext.cnf" <<EOF
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:$DOMAIN,DNS:*.$DOMAIN
EOF

openssl x509 -req \
  -in "$TMPDIR/tls.csr" \
  -CA "$TMPDIR/ca.crt" \
  -CAkey "$TMPDIR/ca.key" \
  -CAcreateserial \
  -out "$TMPDIR/tls.crt" \
  -days 365 \
  -extfile "$TMPDIR/tls-ext.cnf" \
  2>/dev/null

kubectl create secret tls clustral-tls \
  --namespace="$NAMESPACE" \
  --cert="$TMPDIR/tls.crt" \
  --key="$TMPDIR/tls.key" \
  --dry-run=client -o yaml | kubectl apply -f -

echo ""
echo "Done. Created secrets in namespace '$NAMESPACE':"
echo "  - clustral-internal-jwt   (ES256 keypair for internal JWTs)"
echo "  - clustral-kubeconfig-jwt (ES256 keypair for kubeconfig JWTs)"
echo "  - clustral-ca             (Self-signed CA certificate)"
echo "  - clustral-tls            (TLS certificate for $DOMAIN)"
