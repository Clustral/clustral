# Clustral

Kubernetes access proxy — a Teleport alternative built on .NET and React.

Clustral lets users authenticate via any OIDC provider (Keycloak, Auth0, Okta, Azure AD), then transparently proxies `kubectl` traffic through a control plane to registered cluster agents. No inbound firewall rules required on the cluster side.

## Architecture

```
  Browser (HTTPS)                   CLI
      │                              │
      │                              │  auto-discovery
      ▼                              ▼
  ┌─────────────────┐    ┌───────────────────────┐
  │  Web (Next.js)  │───▶│  OIDC Provider        │
  │  :3000 HTTPS    │    │  (Keycloak / Auth0 /  │
  │  Server-side    │    │   Okta / Azure AD)    │
  │  OIDC auth      │    └───────────────────────┘
  └────────┬────────┘
           │ proxies /api/*, /proxy/*
  ┌────────▼────────┐
  │  ControlPlane   │  ASP.NET Core
  │  REST :5000     │  Cluster mgmt, credentials
  │  gRPC :5001     │  Agent tunnel, kubectl proxy
  └────────┬────────┘
           │ bidirectional gRPC stream
  ┌────────▼────────┐
  │  Agent          │  .NET Worker Service (in-cluster)
  │  kubectl proxy  │  Forwards API traffic to k8s API
  └─────────────────┘
```

| Component        | Stack                                       | Description                                     |
|------------------|---------------------------------------------|-------------------------------------------------|
| **Web**          | Next.js 14, React 18, TypeScript, Tailwind  | HTTPS dashboard, server-side OIDC via NextAuth  |
| **ControlPlane** | ASP.NET Core, MongoDB                       | REST + gRPC server, kubectl tunnel proxy        |
| **Agent**        | .NET Worker Service                         | Deployed per cluster, tunnels kubectl traffic   |
| **CLI**          | .NET NativeAOT, System.CommandLine          | `clustral login` / `clustral kube login`        |

## Quick Start (On-Prem)

Deploy the full stack from pre-built images.

### 1. Create `docker-compose.yml`

```yaml
services:
  mongo:
    image: mongo:8
    restart: unless-stopped
    volumes:
      - mongo_data:/data/db
    healthcheck:
      test: ["CMD", "mongosh", "--eval", "db.adminCommand('ping')"]
      interval: 5s
      timeout: 5s
      retries: 10

  keycloak:
    image: quay.io/keycloak/keycloak:24.0
    restart: unless-stopped
    command: start-dev --import-realm
    environment:
      KEYCLOAK_ADMIN: admin
      KEYCLOAK_ADMIN_PASSWORD: admin
      KC_HTTP_PORT: 8080
      KC_HOSTNAME_STRICT: "false"
    ports:
      - "8080:8080"
    volumes:
      - ./keycloak:/opt/keycloak/data/import:ro
    healthcheck:
      test: ["CMD-SHELL", "exec 3<>/dev/tcp/localhost/8080 && echo -e 'GET /health/ready HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -1 <&3 | grep -q '200 OK'"]
      interval: 10s
      timeout: 10s
      retries: 30
      start_period: 60s

  controlplane:
    image: ghcr.io/clustral/clustral-controlplane:latest
    restart: unless-stopped
    depends_on:
      mongo:
        condition: service_healthy
      keycloak:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Clustral: "mongodb://mongo:27017"
      MongoDB__DatabaseName: "clustral"
      Keycloak__Authority: "http://<YOUR_HOST_IP>:8080/realms/clustral"
      Keycloak__MetadataAddress: "http://<YOUR_HOST_IP>:8080/realms/clustral/.well-known/openid-configuration"
      Keycloak__ClientId: "clustral-control-plane"
      Keycloak__Audience: "clustral-control-plane"
      Keycloak__RequireHttpsMetadata: "false"
    ports:
      - "5100:5000"
      - "5101:5001"
    healthcheck:
      test: ["CMD-SHELL", "curl -sf http://localhost:5000/healthz || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 15s

  web:
    image: ghcr.io/clustral/clustral-web:latest
    restart: unless-stopped
    depends_on:
      controlplane:
        condition: service_healthy
    environment:
      NEXTAUTH_URL: "http://<YOUR_HOST_IP>:3000"
      CONTROLPLANE_URL: "http://<YOUR_HOST_IP>:5100"
      OIDC_ISSUER: "http://<YOUR_HOST_IP>:8080/realms/clustral"
      OIDC_CLIENT_ID: "clustral-web"
      OIDC_CLIENT_SECRET: "clustral-web-secret"
      AUTH_SECRET: "change-me-to-a-random-32-char-string!!"
    ports:
      - "3000:3000"

volumes:
  mongo_data:
```

> Replace `<YOUR_HOST_IP>` with your machine's IP address (e.g. `192.168.1.100`). All services must use the same IP so the browser, Next.js server, and ControlPlane can all reach Keycloak at the same issuer URL.

### 2. Download the Keycloak realm config

```bash
mkdir -p keycloak
curl -sL https://raw.githubusercontent.com/Clustral/clustral/main/infra/keycloak/clustral-realm.json \
  -o keycloak/clustral-realm.json
```

### 3. Start

```bash
docker compose up -d
```

### 4. Access

Open `https://<your-ip>:3000` in your browser. Accept the self-signed certificate warning.

### 5. Default users (Keycloak)

| Username | Password | Role             |
|----------|----------|------------------|
| `admin`  | `admin`  | `clustral-admin` |
| `dev`    | `dev`    | `clustral-user`  |

### 6. TLS certificates

The Web container generates a self-signed certificate at startup. For production, mount your own:

```yaml
web:
  volumes:
    - ./certs/tls.crt:/etc/clustral-web/tls.crt:ro
    - ./certs/tls.key:/etc/clustral-web/tls.key:ro
```

## Using a Different OIDC Provider

Clustral works with any OIDC-compliant provider. Just change the Web service environment variables:

### Auth0

```yaml
web:
  environment:
    OIDC_ISSUER: "https://your-tenant.us.auth0.com"
    OIDC_CLIENT_ID: "your-auth0-client-id"
    OIDC_CLIENT_SECRET: "your-auth0-client-secret"
```

### Azure AD

```yaml
web:
  environment:
    OIDC_ISSUER: "https://login.microsoftonline.com/your-tenant-id/v2.0"
    OIDC_CLIENT_ID: "your-azure-client-id"
    OIDC_CLIENT_SECRET: "your-azure-client-secret"
```

### Okta

```yaml
web:
  environment:
    OIDC_ISSUER: "https://your-org.okta.com"
    OIDC_CLIENT_ID: "your-okta-client-id"
    OIDC_CLIENT_SECRET: "your-okta-client-secret"
```

The ControlPlane's `Keycloak__Authority` and `Keycloak__MetadataAddress` must also point to the same OIDC provider so it can validate the JWT tokens.

## Distributed Deployment

Each component can run on a separate host. The Web UI connects to the ControlPlane and OIDC provider via environment variables:

```yaml
web:
  environment:
    CONTROLPLANE_URL: "https://controlplane.example.com"
    OIDC_ISSUER: "https://sso.example.com/realms/clustral"
    OIDC_CLIENT_ID: "clustral-web"
    OIDC_CLIENT_SECRET: "<secret>"
    AUTH_SECRET: "<random-32-chars>"
```

## CLI Usage

```bash
# Authenticate — discovers OIDC settings from the ControlPlane automatically
clustral login app.clustral.example

# List available clusters
clustral kube ls

# Connect to a cluster (writes kubeconfig)
clustral kube login <cluster-id>

# kubectl works transparently
kubectl get pods -A

# Sign out — revokes credentials, removes kubeconfig contexts
clustral logout
```

The CLI auto-discovers OIDC settings via `GET /.well-known/clustral-configuration`. No OIDC provider URL needed.

Build from source (single NativeAOT binary, no runtime dependencies):

```bash
dotnet publish src/Clustral.Cli -r osx-arm64 -c Release    # macOS Apple Silicon
dotnet publish src/Clustral.Cli -r linux-x64  -c Release    # Linux
dotnet publish src/Clustral.Cli -r win-x64    -c Release    # Windows
```

## Deploy an Agent

Register a cluster in the Web UI, then deploy the agent to your Kubernetes cluster:

```bash
# Apply RBAC
kubectl apply -f https://raw.githubusercontent.com/Clustral/clustral/main/src/Clustral.Agent/k8s/serviceaccount.yaml
kubectl apply -f https://raw.githubusercontent.com/Clustral/clustral/main/src/Clustral.Agent/k8s/clusterrole.yaml
kubectl apply -f https://raw.githubusercontent.com/Clustral/clustral/main/src/Clustral.Agent/k8s/clusterrolebinding.yaml

# Create the secret (values from the UI registration step)
kubectl -n clustral create secret generic clustral-agent-config \
  --from-literal=cluster-id="<CLUSTER_ID>" \
  --from-literal=control-plane-url="http://<CONTROLPLANE_HOST>:5001" \
  --from-literal=bootstrap-token="<BOOTSTRAP_TOKEN>"

# Deploy the agent
kubectl apply -f https://raw.githubusercontent.com/Clustral/clustral/main/src/Clustral.Agent/k8s/deployment.yaml

# Check status
kubectl -n clustral logs -f deploy/clustral-agent
```

The agent connects outbound to the ControlPlane gRPC port — no inbound firewall rules needed.

For Docker Desktop Kubernetes, use `host.docker.internal`:

```bash
--from-literal=control-plane-url="http://host.docker.internal:5001"
```

## How It Works

Clustral provides secure, tunneled `kubectl` access to Kubernetes clusters without requiring inbound firewall rules, VPNs, or bastion hosts. It works by placing a lightweight agent inside each cluster that opens an outbound connection to a central control plane. Users authenticate via their organization's identity provider and get short-lived credentials that route `kubectl` commands through this tunnel.

### The Big Picture

```
 Developer workstation                  Clustral ControlPlane               Target cluster
┌──────────────────────┐     HTTPS     ┌─────────────────────┐   gRPC      ┌──────────────┐
│                      │    (REST)     │                     │  tunnel     │              │
│  clustral login      │──────────────▶│  Authenticate user  │◀───────────│  Agent pod   │
│  clustral kube login │──────────────▶│  Issue credential   │            │  (outbound   │
│  kubectl get pods    │──────────────▶│  Proxy through      │───────────▶│   only)      │
│                      │               │  tunnel             │            │              │──▶ k8s API
└──────────────────────┘               └─────────────────────┘            └──────────────┘
                                              ▲
                                              │ OIDC
                                       ┌──────┴──────┐
                                       │   Keycloak  │
                                       │   Auth0     │
                                       │   Okta      │
                                       │   Azure AD  │
                                       └─────────────┘
```

### Step-by-step: Getting access to a cluster

**1. Admin registers a cluster**

Through the Web UI or API, an admin registers a new cluster. Clustral generates a one-time bootstrap token.

**2. Agent is deployed to the cluster**

The agent is deployed as a Kubernetes pod using the bootstrap token. On first boot, it exchanges the bootstrap token for a long-lived agent credential and opens a persistent gRPC bidirectional stream (tunnel) to the ControlPlane. The cluster status changes to "Connected".

```
Agent → gRPC OpenTunnel → ControlPlane
Agent sends AgentHello (cluster ID, k8s version)
ControlPlane sends TunnelHello (ack)
Stream stays open — the agent reconnects automatically with exponential backoff if disconnected.
```

**3. User authenticates**

```bash
$ clustral login app.example.com
```

The CLI discovers OIDC settings from the ControlPlane (`GET /.well-known/clustral-configuration`), opens the browser for SSO login (Authorization Code + PKCE), and stores the JWT in `~/.clustral/token`.

The Web UI does the same via NextAuth.js server-side — the user clicks "Sign in with SSO" and the OIDC flow happens entirely on the server (no CORS issues).

**4. User gets kubectl credentials**

```bash
$ clustral kube ls
  CLUSTER                  ID                                     STATUS         K8S VERSION
> production               a1b2c3d4-...                           Connected      v1.30.2

$ clustral kube login a1b2c3d4-...
Kubeconfig updated. Context: clustral-a1b2c3d4-...
```

The CLI exchanges the JWT for a short-lived kubeconfig credential via `POST /api/v1/auth/kubeconfig-credential`. The credential and server URL are written to `~/.kube/config` — existing contexts are preserved, never overwritten.

**5. kubectl traffic flows through the tunnel**

```bash
$ kubectl get pods -A
```

```
kubectl
  → sends request to ControlPlane (via server URL in kubeconfig)
  → KubectlProxyMiddleware validates the short-lived credential
  → looks up the TunnelSession for the target cluster
  → wraps the HTTP request in an HttpRequestFrame (protobuf)
  → sends it through the gRPC tunnel to the Agent
  → Agent forwards to the in-cluster Kubernetes API server
  → response flows back through the tunnel
  → kubectl displays the result
```

The agent authenticates to the k8s API with its own ServiceAccount — the user's Clustral credential never reaches the cluster. Multiple kubectl requests are multiplexed over the single gRPC stream using request IDs.

**6. User logs out**

```bash
$ clustral logout
  Revoked credential for clustral-a1b2c3d4-...
  Removed kubeconfig context: clustral-a1b2c3d4-...
Logged out.
```

The CLI revokes all kubeconfig credentials server-side, removes all `clustral-*` entries from `~/.kube/config`, and clears the stored JWT.

### Security model

| Layer | Mechanism |
|---|---|
| User → ControlPlane | OIDC JWT (from any provider) |
| kubectl → ControlPlane | Short-lived bearer token (SHA-256 hashed, never stored raw) |
| Agent → ControlPlane | Long-lived agent credential (issued via bootstrap token, rotatable) |
| Agent → k8s API | In-cluster ServiceAccount token (auto-rotated by kubelet) |
| Tunnel transport | gRPC over HTTP/2 (TLS in production) |
| Agent connectivity | Outbound only — no inbound firewall rules needed |

### Token lifecycle

```
Bootstrap token (one-time, from registration)
  → exchanged for agent credential (1 year, rotatable)
    → agent uses it to open gRPC tunnel

OIDC JWT (short-lived, from Keycloak/Auth0/etc.)
  → exchanged for kubeconfig credential (8 hours default, configurable)
    → used by kubectl to authenticate proxy requests
    → revoked on logout
```

### What happens when the agent disconnects

- The TunnelServiceImpl `finally` block sets the cluster status to "Disconnected"
- The TunnelSession is removed from the in-memory registry
- kubectl requests for that cluster return `502: Cluster agent is not connected`
- The agent's reconnect loop kicks in with exponential backoff + jitter
- When reconnected, the cluster status returns to "Connected"

## Repository Layout

```
clustral/
├── src/
│   ├── Clustral.ControlPlane/   # ASP.NET Core — REST + gRPC + kubectl proxy
│   ├── Clustral.Agent/          # .NET Worker Service — tunnel + kubectl proxy
│   ├── Clustral.Cli/            # NativeAOT console app — login + kubeconfig
│   └── Clustral.Web/            # Next.js 14 — dashboard, server-side OIDC
├── packages/
│   ├── Clustral.Sdk/            # Shared: TokenCache, KubeconfigWriter, GrpcChannelFactory
│   └── proto/                   # Protobuf contracts (ClusterService, TunnelService, AuthService)
├── infra/
│   └── keycloak/                # Realm export with pre-configured clients and users
├── .github/workflows/           # CI/CD — build, test, multi-arch image push to ghcr.io
├── docker-compose.yml           # Dev stack (builds from source)
└── CLAUDE.md                    # Claude Code guide
```

## Container Images

Published to GitHub Container Registry when relevant source files change:

| Image | Architectures |
|---|---|
| `ghcr.io/clustral/clustral-controlplane` | `linux/amd64`, `linux/arm64` |
| `ghcr.io/clustral/clustral-agent` | `linux/amd64`, `linux/arm64` |
| `ghcr.io/clustral/clustral-web` | `linux/amd64`, `linux/arm64` |

Tags: `latest`, `main`, commit SHA, semver (`v1.0.0`, `v1.0`) on tagged releases.

## Web UI Environment Variables

All configuration is via runtime environment variables — no rebuild needed.

| Variable | Required | Default | Description |
|---|---|---|---|
| `NEXTAUTH_URL` | Yes | — | Browser-facing URL of the Web UI (e.g. `http://192.168.1.100:3000`) |
| `CONTROLPLANE_URL` | Yes | `http://localhost:5000` | ControlPlane REST API URL (reachable from the web container) |
| `OIDC_ISSUER` | Yes | — | OIDC provider discovery URL (reachable from the web container) |
| `OIDC_CLIENT_ID` | No | `clustral-web` | OIDC client ID |
| `OIDC_CLIENT_SECRET` | Yes | — | OIDC client secret (confidential client) |
| `AUTH_SECRET` | Yes | — | NextAuth session encryption key (random 32+ chars) |

## Development

### Prerequisites

- Docker Desktop (or OrbStack)
- .NET 10 SDK
- Node.js 20+ and bun
- kubectl (for agent testing)

### Run from source

```bash
# Start infrastructure + applications (builds locally)
docker compose up -d

# Or run infrastructure only and applications natively:
docker compose up -d mongo keycloak

# ControlPlane
dotnet run --project src/Clustral.ControlPlane

# Web UI (Next.js dev server)
cd src/Clustral.Web && bun install && bun dev

# Agent (against Docker Desktop Kubernetes)
dotnet run --project src/Clustral.Agent -- \
  --Agent:ClusterId="<ID>" \
  --Agent:ControlPlaneUrl=http://localhost:5001 \
  --Agent:BootstrapToken="<TOKEN>"
```

### Run tests

```bash
dotnet test Clustral.slnx
```

## Keycloak Configuration

The realm export at `infra/keycloak/clustral-realm.json` pre-configures:

| Client                   | Type          | Purpose                                         |
|--------------------------|---------------|--------------------------------------------------|
| `clustral-control-plane` | Bearer-only   | JWT validation by the ControlPlane               |
| `clustral-cli`           | Public        | CLI PKCE flow, redirect to `127.0.0.1:7777`     |
| `clustral-web`           | Confidential  | Web UI server-side OIDC via NextAuth             |

## License

Copyright (c) 2026 KubeIT. All rights reserved. See [LICENSE](LICENSE).
