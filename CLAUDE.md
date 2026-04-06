# Clustral — Claude Code Guide

Clustral is an open-source Kubernetes access proxy (Teleport alternative) built entirely on .NET 9. It lets users authenticate via Keycloak, then transparently proxies `kubectl` traffic through a control plane to registered cluster agents.

---

## Repository Layout

```
clustral/
├── src/
│   ├── Clustral.ControlPlane/   # ASP.NET Core — REST + gRPC server, EF Core, Keycloak OIDC
│   ├── clustral-agent/          # Go 1.23 service — gRPC client, kubectl reverse proxy, Helm-deployed
│   ├── Clustral.Cli/            # System.CommandLine, NativeAOT — clustral login / clustral kube login
│   └── Clustral.Web/            # Vite + React 18 + TypeScript, shadcn/ui, TanStack Query, Zustand
├── packages/
│   ├── Clustral.Sdk/            # Shared: TokenCache, KubeconfigWriter, GrpcChannelFactory
│   └── proto/                   # .proto contracts: ClusterService, TunnelService, AuthService
├── infra/
│   ├── helm/                    # Agent Helm chart
│   └── k8s/                     # Local kind cluster manifests for dev
├── Directory.Packages.props     # Central package version management
├── docker-compose.yml
└── CLAUDE.md
```

---

## Architecture Overview

```
  clustral CLI
      │  OIDC device flow → Keycloak
      │  JWT stored in TokenCache
      │  kubeconfig written via KubeconfigWriter
      ▼
  ControlPlane  (ASP.NET Core)
      │  REST  — Web UI + CLI management calls
      │  gRPC  — TunnelService (bidirectional streaming to agents)
      │  EF Core → PostgreSQL (clusters, users, audit log)
      │  Keycloak OIDC — token introspection / JWKS validation
      ▼
  Agent  (Go service, runs in-cluster via Helm)
      │  gRPC client → TunnelService on ControlPlane
      │  Receives proxied kubectl HTTP traffic over the tunnel
      │  Forwards locally to the Kubernetes API server
      ▼
  Kubernetes API Server
```

Key flows:
- **clustral login** — OIDC device-code flow against Keycloak, writes token to `~/.clustral/token`.
- **clustral kube login** — exchanges the stored token for a short-lived kubeconfig entry that routes through the ControlPlane tunnel.
- **Tunnel** — agent opens a persistent gRPC stream to ControlPlane; kubectl traffic is multiplexed over that stream so no inbound firewall rules are needed on the cluster side.
- **clustral access request** — creates a JIT (just-in-time) access request for a role on a cluster. Admin approves or denies via CLI or Web UI. On approval, `clustral kube login` works with a time-limited credential. Access is automatically expired/revoked when the grant window closes.

---

## Local Dev Setup

### Prerequisites

- Docker Desktop (or OrbStack)
- .NET 9 SDK
- Node.js 20+ and bun
- `kind` and `kubectl`

### Start backing services

```bash
docker-compose up -d
```

This starts:
| Service    | Port  | Notes                        |
|------------|-------|------------------------------|
| PostgreSQL | 5432  | db=clustral, user=clustral, pw=clustral |
| Keycloak   | 8080  | realm imported from `infra/keycloak/` |

### ControlPlane

```bash
cd src/Clustral.ControlPlane
dotnet run
# Listens: HTTP :5000, gRPC :5001
```

### Agent (local, targeting kind)

```bash
# Create local kind cluster first (one-time)
kind create cluster --config infra/k8s/kind-config.yaml

cd src/clustral-agent
go run . --control-plane-url=http://localhost:5001
```

### CLI

```bash
cd src/Clustral.Cli
dotnet run -- login
dotnet run -- logout
dotnet run -- kube login --cluster <name>
dotnet run -- kube logout <cluster>
dotnet run -- kube list
dotnet run -- clusters list
dotnet run -- users list
dotnet run -- roles list
dotnet run -- access request --role <name> --cluster <name>
dotnet run -- access list
dotnet run -- access approve <id>
dotnet run -- access deny <id> --reason "..."
dotnet run -- access revoke <id>
```

NativeAOT publish:
```bash
dotnet publish -r osx-arm64 -c Release
```

### Web UI

```bash
cd src/Clustral.Web
bun install
bun dev
# Listens: http://localhost:5173
```

The Web UI includes pages for clusters, users, roles, and access requests management.

---

## Database Migrations

All EF Core work lives in `src/Clustral.ControlPlane`.

```bash
# Add a migration
dotnet ef migrations add <MigrationName> --project src/Clustral.ControlPlane

# Apply to local DB
dotnet ef database update --project src/Clustral.ControlPlane

# Generate SQL script (for production / review)
dotnet ef migrations script --idempotent --project src/Clustral.ControlPlane -o infra/migrations/latest.sql
```

Migration files live in `src/Clustral.ControlPlane/Migrations/`. Never edit generated migration files by hand; add a new migration instead.

---

## Proto Contracts

`.proto` files live in `packages/proto/`. Generated C# stubs are output to `packages/Clustral.Sdk/Generated/` via the `Grpc.Tools` MSBuild integration — do not edit generated files.

After editing a `.proto` file, rebuild the Sdk project:
```bash
dotnet build packages/Clustral.Sdk
```

Services:
- **ClusterService** — CRUD for registered clusters (REST-facing, used by Web + CLI)
- **TunnelService** — bidirectional streaming; agent ↔ ControlPlane kubectl tunnel
- **AuthService** — token exchange and validation helpers

---

## Naming Conventions

| Scope | Convention |
|---|---|
| C# namespaces | `Clustral.<Project>.<Feature>` (e.g. `Clustral.ControlPlane.Clusters`) |
| C# types | PascalCase; no `I` prefix on interfaces except service contracts |
| gRPC service methods | PascalCase verbs: `GetCluster`, `RegisterAgent`, `OpenTunnel` |
| REST routes | kebab-case, versioned: `/api/v1/clusters/{id}` |
| EF Core entities | Singular noun, no `Entity` suffix (e.g. `Cluster`, `AuditLog`) |
| EF Core tables | Plural snake_case via `UseSnakeCaseNamingConvention()` |
| React components | PascalCase files under `src/components/` |
| Zustand stores | camelCase, file named `use<Store>Store.ts` |
| TanStack Query keys | array-of-strings, colocated with the hook |
| Helm values | camelCase |
| Proto message names | PascalCase, no namespace prefix (proto package provides namespacing) |
| Result error codes | SCREAMING_SNAKE_CASE (e.g. `ROLE_NOT_FOUND`) via `ResultErrors.*` catalog |
| Controller result mapping | Private methods return `Result<T>`, mapped to HTTP via `ToActionResult()` |

---

## Testing

524 tests across 3 projects. Run all with:
```bash
dotnet test Clustral.slnx
```

- **Unit tests**: `*.Tests` projects alongside each `src/` project.
- **Integration tests**: `src/Clustral.ControlPlane.IntegrationTests` — uses Testcontainers (MongoDB) + `WebApplicationFactory`; requires Docker running. Do not mock the database.
- **Web tests**: `src/Clustral.Web` uses Vitest (`bun test`) and Playwright for e2e (`bun e2e`).

---

## How Claude Code Should Approach Tasks Here

- **Read before editing.** Always read the relevant file(s) before proposing changes. Do not guess at existing signatures or table schemas.
- **Proto changes are API changes.** Editing `.proto` files affects both ControlPlane and Agent. Note the downstream impact explicitly before making changes.
- **EF Core migrations are append-only.** Never edit existing migration files. Add a new migration for any schema change.
- **NativeAOT constraints apply to the CLI.** Avoid reflection, `dynamic`, and runtime code generation in `Clustral.Cli` and anything in `Clustral.Sdk` that the CLI uses. Prefer source generators.
- **gRPC stubs are generated.** Files under `*/Generated/` must not be edited manually.
- **Keycloak realm config is in `infra/keycloak/`.** If adding a new OAuth scope or client, the realm export there must be updated too.
- **Web state management**: local/ephemeral UI state → React `useState`; server state → TanStack Query; cross-component shared state → Zustand. Do not reach for Zustand for data that TanStack Query already owns.
- **Helm chart changes** in `infra/helm/` must keep `values.yaml` as the source of truth; do not hardcode values in templates.
- **Security-sensitive paths** (token handling in `TokenCache`, tunnel auth in `TunnelService`) require extra care. Flag any change that touches these for explicit review rather than silently implementing.
- **Keep PRs focused.** Proto change, migration, and implementation should be reviewable together but should be called out as distinct layers in the PR description.
- **Use `Result<T>` in controllers** instead of throwing exceptions. Use `ResultErrors.*` catalog for consistent error codes across the codebase.
- **CLI error display** uses `CliErrors.*` card-style display for user-friendly error output.
- **Command aliases**: all listing commands support both `list` and `ls` aliases.
- **xUnit test output**: use `ITestOutputHelper` in xUnit tests, not `Console.WriteLine`.
- **Integration tests need Docker** running for Testcontainers.
