#!/bin/sh
# Clustral CLI installer for Linux and macOS.
#
# Usage:
#   curl -sL https://raw.githubusercontent.com/Clustral/clustral/main/install.sh | sh
#   curl -sL https://raw.githubusercontent.com/Clustral/clustral/main/install.sh | sh -s -- --pre
#   curl -sL https://raw.githubusercontent.com/Clustral/clustral/main/install.sh | sh -s -- --version v0.1.0

set -e

REPO="Clustral/clustral"
INSTALL_DIR="${INSTALL_DIR:-/usr/local/bin}"
PRE=false
VERSION=""

for arg in "$@"; do
  case "$arg" in
    --pre) PRE=true ;;
    --version) shift; VERSION="$1" ;;
    v*) VERSION="$arg" ;;
  esac
done

# Detect OS and architecture.
OS=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)

case "$ARCH" in
  x86_64)         ARCH="amd64" ;;
  aarch64|arm64)  ARCH="arm64" ;;
  *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

case "$OS" in
  linux)  BINARY="clustral-linux-$ARCH" ;;
  darwin) BINARY="clustral-darwin-$ARCH" ;;
  *) echo "Unsupported OS: $OS (use install.ps1 for Windows)"; exit 1 ;;
esac

# Resolve download URL.
if [ -n "$VERSION" ]; then
  TAG="$VERSION"
  URL="https://github.com/$REPO/releases/download/$TAG/$BINARY"
elif [ "$PRE" = true ]; then
  echo "Fetching latest pre-release..."
  URL=$(curl -sL "https://api.github.com/repos/$REPO/releases" \
    | grep -o "https://github.com/$REPO/releases/download/[^\"]*/$BINARY" \
    | head -1)
  TAG=$(echo "$URL" | grep -o 'v[^/]*')
else
  echo "Fetching latest stable release..."
  URL=$(curl -sL "https://api.github.com/repos/$REPO/releases/latest" \
    | grep -o "https://github.com/$REPO/releases/download/[^\"]*/$BINARY" \
    | head -1)
  TAG=$(echo "$URL" | grep -o 'v[^/]*')
fi

if [ -z "$URL" ]; then
  echo "Error: Could not find a release for $BINARY."
  echo "Check https://github.com/$REPO/releases for available versions."
  exit 1
fi

echo "Installing clustral $TAG ($BINARY)..."
echo "  → $INSTALL_DIR/clustral"

# Download and install.
if command -v sudo >/dev/null 2>&1 && [ ! -w "$INSTALL_DIR" ]; then
  curl -sL "$URL" -o /tmp/clustral
  chmod +x /tmp/clustral
  sudo mv /tmp/clustral "$INSTALL_DIR/clustral"
else
  curl -sL "$URL" -o "$INSTALL_DIR/clustral"
  chmod +x "$INSTALL_DIR/clustral"
fi

echo ""
echo "clustral installed successfully!"
"$INSTALL_DIR/clustral" --version 2>/dev/null || true
echo ""
echo "Get started:"
echo "  clustral login <your-controlplane-url>"
