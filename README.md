# Clustral

Kubernetes access proxy — a Teleport alternative built on .NET and React.

Clustral lets users authenticate via Keycloak, then transparently proxies `kubectl` traffic through a control plane to registered cluster agents. No inbound firewall rules required on the cluster side.

## Architecture

```
  Browser / CLI
      │
      │  OIDC (Keycloak)
      ▼
  ┌──────────────────────┐
  │   ControlPlane       │  ASP.NET Core
  │   REST :5000         │  Cluster management, credential issuance
  │   gRPC :5001         │  Agent tunnel, kubectl proxy
  └──────────┬───────────┘
             │ bidirectional gRPC stream
  ┌──────────▼───────────┐
  │   Agent              │  .NET Worker Service (in-cluster)
  │   kubectl proxy      │  Forwards API traffic to k8s API server
  └──────────────────────┘
```

| Component        | Stack                                    | Description                                       |
|------------------|------------------------------------------|-------------------------------------------------|
| **ControlPlane** | ASP.NET Core, MongoDB, Keycloak OIDC     | REST + gRPC server, kubectl tunnel proxy          |
| **Agent**        | .NET Worker Service                      | Deployed per cluster, tunnels kubectl traffic     |
| **CLI**          | .NET NativeAOT, System.CommandLine       | `clustral login` / `clustral kube login`          |
| **Web**          | Vite, React 18, TypeScript, Tailwind CSS | Dashboard — cluster management, OIDC login        |

## Quick Start (On-Prem)

Deploy the full stack from pre-built images with a single `docker compose up`.

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
      KC_HOSTNAME: localhost
      KC_HOSTNAME_PORT: 8080
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
      Keycloak__Authority: "http://localhost:8080/realms/clustral"
      Keycloak__MetadataAddress: "http://keycloak:8080/realms/clustral/.well-known/openid-configuration"
      Keycloak__ClientId: "clustral-control-plane"
      Keycloak__Audience: "clustral-control-plane"
      Keycloak__RequireHttpsMetadata: "false"
    ports:
      - "5000:5000"
      - "5001:5001"
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
    ports:
      - "3000:3000"

volumes:
  mongo_data:
```

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

| Service        | URL                    | Notes                    |
|----------------|------------------------|--------------------------|
| Web UI         | http://localhost:3000   | Dashboard                |
| ControlPlane   | http://localhost:5000   | REST API + Swagger       |
| ControlPlane   | localhost:5001          | gRPC (agent tunnel)      |
| Keycloak       | http://localhost:8080   | Admin: admin/admin       |

### 5. Default users (Keycloak)

| Username | Password | Role             |
|----------|----------|------------------|
| `admin`  | `admin`  | `clustral-admin` |
| `dev`    | `dev`    | `clustral-user`  |

## CLI Usage

```bash
# Authenticate — discovers Keycloak from the ControlPlane automatically
clustral login localhost:3000

# Or for a production deployment
clustral login app.clustral.example

# Get kubeconfig credentials for a cluster
clustral kube login <cluster-id>

# kubectl works transparently
kubectl get namespaces
```

The CLI is a single NativeAOT binary with no runtime dependencies. Build it from source:

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

The agent connects outbound to the ControlPlane — no inbound firewall rules needed.

## How It Works

### Authentication

1. User runs `clustral login <controlplane-url>` (or signs in via the Web UI)
2. CLI discovers Keycloak settings from `GET /.well-known/clustral-configuration`
3. Browser opens for Keycloak OIDC login (Authorization Code + PKCE)
4. JWT is stored in `~/.clustral/token`

### kubectl Access

1. User runs `clustral kube login <cluster-id>`
2. CLI exchanges JWT for a short-lived credential via ControlPlane REST API
3. Credential is written to `~/.kube/config` (existing contexts are preserved)
4. `kubectl` commands route through the ControlPlane tunnel to the agent

### Agent Tunnel

1. Agent (deployed in-cluster) opens a persistent gRPC bidirectional stream to the ControlPlane
2. kubectl HTTP requests are multiplexed over the stream — no inbound ports needed
3. Agent strips the user's token and authenticates to the k8s API with its own ServiceAccount
4. Cluster status is tracked: Connected while tunnel is open, Disconnected when agent stops

## Repository Layout

```
clustral/
├── src/
│   ├── Clustral.ControlPlane/   # ASP.NET Core — REST + gRPC + kubectl proxy
│   ├── Clustral.Agent/          # .NET Worker Service — tunnel + kubectl proxy
│   ├── Clustral.Cli/            # NativeAOT console app — login + kubeconfig
│   └── Clustral.Web/            # Vite + React 18 + TypeScript — dashboard
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

Published to GitHub Container Registry on every push to `main`:

| Image | Architectures |
|---|---|
| `ghcr.io/clustral/clustral-controlplane` | `linux/amd64`, `linux/arm64` |
| `ghcr.io/clustral/clustral-agent` | `linux/amd64`, `linux/arm64` |
| `ghcr.io/clustral/clustral-web` | `linux/amd64`, `linux/arm64` |

Tags: `latest`, `main`, commit SHA, semver (`v1.0.0`, `v1.0`) on tagged releases.

## Development

### Prerequisites

- Docker Desktop (or OrbStack)
- .NET 10 SDK
- Node.js 20+ and bun
- kind and kubectl (for agent testing)

### Run from source

```bash
# Start infrastructure + applications (builds locally)
docker compose up -d

# Or run infrastructure only and applications natively:
docker compose up -d mongo keycloak

# ControlPlane
dotnet run --project src/Clustral.ControlPlane

# Web UI (proxies /api to ControlPlane)
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
cd src/Clustral.Web && bun test
```

## Keycloak Configuration

The realm export at `infra/keycloak/clustral-realm.json` pre-configures:

| Client                   | Type        | Purpose                                         |
|--------------------------|-------------|--------------------------------------------------|
| `clustral-control-plane` | Bearer-only | JWT validation by the ControlPlane               |
| `clustral-cli`           | Public      | CLI PKCE flow, redirect to `127.0.0.1:7777`     |
| `clustral-web`           | Public      | Web UI OIDC, redirect to `localhost:5173/3000`   |

## License

Copyright (c) 2026 KubeIT. All rights reserved. See [LICENSE](LICENSE).
