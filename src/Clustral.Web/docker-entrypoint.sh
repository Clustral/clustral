#!/bin/sh
set -e

CERT_DIR=/etc/nginx/certs

# Generate a self-signed certificate if none is mounted.
if [ ! -f "$CERT_DIR/tls.crt" ] || [ ! -f "$CERT_DIR/tls.key" ]; then
  echo "No TLS certificate found — generating self-signed cert..."
  mkdir -p "$CERT_DIR"
  apk add --no-cache openssl > /dev/null 2>&1 || true
  openssl req -x509 -nodes -days 365 \
    -newkey rsa:2048 \
    -keyout "$CERT_DIR/tls.key" \
    -out "$CERT_DIR/tls.crt" \
    -subj "/CN=clustral" \
    -addext "subjectAltName=DNS:clustral,DNS:localhost,IP:0.0.0.0" \
    2>/dev/null
  echo "Self-signed certificate generated."
fi

exec nginx -g "daemon off;"
