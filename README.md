# Clustral

Open-source Kubernetes access proxy — a Teleport alternative built on .NET and React.

Clustral lets users authenticate via Keycloak, then transparently proxies `kubectl` traffic through a control plane to
registered cluster agents. No inbound firewall rules required on the cluster side.

## Architecture

```
  Browser / CLI
      │
      │  OIDC (Keycloak)
      ▼
  ┌──────────────────────┐
  │   ControlPlane       │  ASP.NET Core
  │   REST :5000         │  Cluster management, credential issuance
  │   gRPC :5001         │  Agent tunnel, auth validation
  └──────────┬───────────┘
             │ bidirectional gRPC stream
  ┌──────────▼───────────┐
  │   Agent              │  .NET Worker Service (in-cluster)
  │   kubectl proxy      │  Forwards API traffic to k8s API server
  └──────────────────────┘
```

| Component        | Stack                                    | Description                                   |
|------------------|------------------------------------------|-----------------------------------------------|
| **ControlPlane** | ASP.NET Core, EF Core, PostgreSQL        | REST API + gRPC server, Keycloak OIDC         |
| **Agent**        | .NET Worker Service                      | Deployed per cluster, tunnels kubectl traffic |
| **CLI**          | .NET NativeAOT, System.CommandLine       | `clustral login` / `clustral kube login`      |
| **Web**          | Vite, React 18, TypeScript, Tailwind CSS | Dashboard for cluster overview                |

## Quick Start

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or [OrbStack](https://orbstack.dev/))
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for local dev outside Docker)
- [Node.js 20+](https://nodejs.org/) and [bun](https://bun.sh/) (for web UI dev)
- [kind](https://kind.sigs.k8s.io/) and [kubectl](https://kubernetes.io/docs/tasks/tools/) (for agent testing)

### Deploy everything with Docker Compose

```bash
docker compose up -d
```

This starts:

| Service           | URL                   | Notes                                   |
|-------------------|-----------------------|-----------------------------------------|
| PostgreSQL        | `localhost:5432`      | db=clustral, user=clustral, pw=clustral |
| Keycloak          | http://localhost:8080 | Admin: admin/admin                      |
| ControlPlane      | http://localhost:5000 | REST API + Swagger                      |
| ControlPlane gRPC | localhost:5001        | Agent tunnel endpoint                   |
| Web UI            | http://localhost:3000 | Dashboard                               |

### Default Users (Keycloak)

| Username | Password | Role                                               |
|----------|----------|----------------------------------------------------|
| `admin`  | `admin`  | `clustral-admin` — full access                     |
| `dev`    | `dev`    | `clustral-user` — list clusters, issue credentials |

### Verify

```bash
# Check all services are healthy
docker compose ps

# Open the Swagger UI
open http://localhost:5000/swagger

# Open the Web UI
open http://localhost:3000

# Open Keycloak admin console
open http://localhost:8080/admin
```

## Local Development (without Docker)

For faster iteration, run infra in Docker and applications natively:

```bash
# Start only PostgreSQL + Keycloak
docker compose up -d postgres keycloak

# ControlPlane
cd src/Clustral.ControlPlane
dotnet run
# → REST http://localhost:5000, gRPC localhost:5001

# Web UI (separate terminal)
cd src/Clustral.Web
bun install
bun dev
# → http://localhost:5173 (proxies /api to :5000)

# Agent (separate terminal, requires kind cluster)
kind create cluster --config infra/k8s/kind-config.yaml
cd src/Clustral.Agent
dotnet run -- \
  --Agent:ClusterId=<your-cluster-id> \
  --Agent:ControlPlaneUrl=http://localhost:5001 \
  --Agent:BootstrapToken=<token-from-register>
```

## CLI Usage

```bash
# Build the CLI (NativeAOT binary)
dotnet publish src/Clustral.Cli -r osx-arm64 -c Release

# Authenticate via browser (OIDC PKCE)
clustral login --authority http://localhost:8080/realms/clustral

# Get kubeconfig credentials for a cluster
clustral kube login <cluster-id>

# Use kubectl normally — context is set automatically
kubectl get namespaces
```

### CLI Configuration

Create `~/.clustral/config.json` to avoid repeating flags:

```json
{
  "oidcAuthority": "http://localhost:8080/realms/clustral",
  "oidcClientId": "clustral-cli",
  "oidcScopes": "openid email profile",
  "controlPlaneUrl": "http://localhost:5000",
  "callbackPort": 7777,
  "insecureTls": true
}
```

## Repository Layout

```
clustral/
├── src/
│   ├── Clustral.ControlPlane/   # ASP.NET Core — REST + gRPC
│   ├── Clustral.Agent/          # .NET Worker Service — tunnel + kubectl proxy
│   ├── Clustral.Cli/            # NativeAOT console app
│   └── Clustral.Web/            # Vite + React 18 + TypeScript
├── packages/
│   ├── Clustral.Sdk/            # Shared: TokenCache, KubeconfigWriter, GrpcChannelFactory
│   └── proto/                   # Protobuf contracts (ClusterService, TunnelService, AuthService)
├── infra/
│   └── keycloak/                # Realm export with pre-configured clients and users
├── docker-compose.yml           # Full stack: postgres + keycloak + controlplane + web
└── CLAUDE.md                    # Claude Code guide
```

## Key Flows

### Authentication

1. User runs `clustral login` (or signs in via the Web UI)
2. Browser opens Keycloak login page (OIDC Authorization Code + PKCE)
3. JWT is stored in `~/.clustral/token`

### Kubectl Access

1. User runs `clustral kube login <cluster-id>`
2. CLI exchanges JWT for a short-lived credential via ControlPlane REST API
3. Credential is written to `~/.kube/config` with context `clustral-<cluster-id>`
4. `kubectl` commands are routed through the ControlPlane tunnel to the cluster agent

### Agent Tunnel

1. Agent (deployed in-cluster via Helm) opens a persistent gRPC stream to ControlPlane
2. kubectl HTTP requests are multiplexed over the stream — no inbound ports needed
3. Agent strips the user's token and authenticates to the k8s API with its own ServiceAccount

## Keycloak Configuration

The realm export at `infra/keycloak/clustral-realm.json` pre-configures:

| Client                   | Type        | Purpose                                                      |
|--------------------------|-------------|--------------------------------------------------------------|
| `clustral-control-plane` | Bearer-only | JWT validation by the ControlPlane                           |
| `clustral-cli`           | Public      | CLI PKCE flow, redirect to `127.0.0.1:7777`                  |
| `clustral-web`           | Public      | Web UI OIDC, redirect to `localhost:5173` / `localhost:3000` |

To modify clients, scopes, or roles:

1. Edit the realm in the Keycloak admin console (http://localhost:8080/admin)
2. Export: **Realm Settings → Action → Partial Export** (include clients and roles)
3. Save the export to `infra/keycloak/clustral-realm.json`

## Database

PostgreSQL with EF Core migrations:

```bash
# Add a migration
dotnet ef migrations add <Name> --project src/Clustral.ControlPlane

# Apply migrations
dotnet ef database update --project src/Clustral.ControlPlane

# Generate idempotent SQL for production
dotnet ef migrations script --idempotent \
  --project src/Clustral.ControlPlane \
  -o infra/migrations/latest.sql
```

In development mode, the ControlPlane auto-applies migrations on startup.

## Proto Contracts

`.proto` files in `packages/proto/` define the gRPC API. After editing:

```bash
dotnet build packages/Clustral.Sdk
```

Services: `ClusterService` (CRUD), `TunnelService` (bidirectional streaming), `AuthService` (credential management).

## License

MIT
