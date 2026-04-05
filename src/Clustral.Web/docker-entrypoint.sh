#!/bin/sh
set -e

CERT_DIR=${TLS_CERT_DIR:-/etc/clustral-web}
CERT_PATH=${TLS_CERT_PATH:-$CERT_DIR/tls.crt}
KEY_PATH=${TLS_KEY_PATH:-$CERT_DIR/tls.key}

# Generate self-signed cert if none is mounted.
if [ ! -f "$CERT_PATH" ] || [ ! -f "$KEY_PATH" ]; then
  echo "No TLS certificate found — generating self-signed cert..."
  mkdir -p "$CERT_DIR"
  openssl req -x509 -nodes -days 365 \
    -newkey rsa:2048 \
    -keyout "$KEY_PATH" \
    -out "$CERT_PATH" \
    -subj "/CN=clustral" \
    -addext "subjectAltName=DNS:clustral,DNS:localhost,IP:0.0.0.0" \
    2>/dev/null
  echo "Self-signed certificate generated."
fi

export TLS_CERT_PATH="$CERT_PATH"
export TLS_KEY_PATH="$KEY_PATH"

exec node https-server.js
