# Clustral — Claude Code Guide

Clustral is an open-source Kubernetes access proxy (Teleport alternative) built entirely on .NET 9. It lets users authenticate via any OIDC provider (Keycloak, Auth0, Okta, Azure AD), then transparently proxies `kubectl` traffic through a control plane to registered cluster agents.

---

## Repository Layout

```
clustral/
├── src/
│   ├── Clustral.ControlPlane/   # ASP.NET Core — REST + gRPC server, MongoDB, OIDC
│   ├── clustral-agent/          # Go 1.23 service — gRPC client, kubectl reverse proxy, Helm-deployed
│   ├── Clustral.Cli/            # System.CommandLine, NativeAOT — clustral login / clustral kube login
│   └── Clustral.Web/            # Vite + React 18 + TypeScript, shadcn/ui, TanStack Query, Zustand
├── packages/
│   ├── Clustral.Sdk/            # Shared: TokenCache, KubeconfigWriter, GrpcChannelFactory
│   └── proto/                   # .proto contracts: ClusterService, TunnelService, AuthService
├── infra/
│   ├── helm/                    # Agent Helm chart
│   ├── nginx/                   # nginx gateway config (TLS termination, routing)
│   └── k8s/                     # Local kind cluster manifests for dev
├── Directory.Packages.props     # Central package version management
├── docker-compose.yml
└── CLAUDE.md
```

---

## Architecture Overview

```
  clustral CLI
      │  GET /.well-known/clustral-configuration → nginx :443 → Web UI (discovery)
      │  Receives: controlPlaneUrl (nginx URL), OIDC settings
      │  All subsequent calls go through nginx :443 → ControlPlane
      │  OIDC PKCE flow → OIDC Provider
      │  JWT stored in TokenCache
      │  kubeconfig written via KubeconfigWriter
      ▼
  nginx  (unified gateway)
      │  :443 HTTPS  — REST API + kubectl proxy → ControlPlane :5000
      │                Web UI pages → Web UI :3000
      │  :5443 gRPC/TLS — L4 TCP passthrough → ControlPlane :5001
      │  :5444 gRPC     — L4 TCP passthrough (local dev only)
      ▼
  ControlPlane  (ASP.NET Core)
      │  REST :5000  — CLI + Web UI management calls (via nginx)
      │  gRPC :5001  — TunnelService (bidirectional streaming to agents)
      │  MongoDB (clusters, users, audit log)
      │  OIDC — token introspection / JWKS validation
      ▼
  Agent  (Go service, runs in-cluster via Helm)
      │  gRPC client → nginx :5443 (TLS) → ControlPlane :5001
      │  Receives proxied kubectl HTTP traffic over the tunnel
      │  Forwards locally to the Kubernetes API server
      ▼
  Kubernetes API Server
```

Key flows:
- **clustral login** — discovers ControlPlane URL and OIDC settings via Web UI's `/.well-known/clustral-configuration` (through nginx), then runs OIDC PKCE flow, writes token to `~/.clustral/token`. All subsequent CLI calls go through nginx to the ControlPlane.
- **clustral kube login** — exchanges the stored token for a short-lived kubeconfig entry that routes through the ControlPlane tunnel.
- **Tunnel** — agent opens a persistent gRPC stream through nginx :5443 (TLS, L4 TCP passthrough) to ControlPlane; kubectl traffic is multiplexed over that stream so no inbound firewall rules are needed on the cluster side.
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
export AGENT_CONTROL_PLANE_URL=http://localhost:5001
go run .
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

687 tests across 3 .NET projects + 35 Go tests. Run all with:
```bash
dotnet test Clustral.slnx
cd src/clustral-agent && go test -race ./...
```

- **Unit tests**: `*.Tests` projects alongside each `src/` project.
- **Integration tests**: `src/Clustral.ControlPlane.Tests/Integration/` — uses Testcontainers (MongoDB) + `WebApplicationFactory`; requires Docker running. Do not mock the database.
- **gRPC integration tests**: `GrpcClusterServiceTests` and `GrpcAuthServiceTests` test all gRPC endpoints using `Grpc.Net.Client` against the test server. Cover register, list, get, update status, deregister, credential issuance/validation/rotation/revocation, and bootstrap token single-use.
- **CLI integration tests**: `CliIntegrationTests` verify CLI wire types deserialize correctly against the real ControlPlane API.
- **Web tests**: `src/Clustral.Web` uses Vitest (`bun test`) and Playwright for e2e (`bun e2e`).

---

## Versioning

- **Git tags are the sole version source** — no `VERSION` file. All components derive their version from `git describe --tags` at build time. Local dev defaults to `0.0.0-dev`.
- **ControlPlane** exposes version via `GET /api/v1/config` (unauthenticated) and `/healthz/detail` (authenticated).
- **Agent** reports both agent version and k8s API version in `AgentHello` handshake — stored on `Cluster.AgentVersion` and `Cluster.KubernetesVersion`, shown in cluster listings. Version is discovered once at startup via `GET /version` on the k8s API server, not sent on every heartbeat.
- **CLI** `clustral version` shows CLI + ControlPlane versions with graceful degradation.
- **Version mismatch** — ControlPlane logs a WARNING when agent version differs. Visible in cluster list tables.

---

## CI/CD Conventions

- **Conventional commits** — use `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `ci:`, `chore:` prefixes. git-cliff generates changelogs from these.
- **Tag scheme** — `v<semver>` (e.g., `v1.0.0`, `v0.2.0-alpha.1`). Pre-release channels: `-alpha`, `-beta`, `-rc`.
- **Changelog** — auto-generated by git-cliff on tag push. `CHANGELOG.md` is committed back to `main` by CI. Do not edit it manually.
- **Docker tags** — `latest` only on stable releases. Pre-releases get channel floating tags (`alpha`, `beta`, `rc`). Stable releases get `version`, `major.minor`, `major`, and `latest`.
- **CI path filtering** — only affected components are built/tested. `.github/workflows/build.yml` uses `dorny/paths-filter` to detect which components changed.
- **Test reporting** — `.trx` results uploaded to GitHub Checks via `dorny/test-reporter`. Code coverage artifacts uploaded for both .NET and Go.

---

## How Claude Code Should Approach Tasks Here

- **Read before editing.** Always read the relevant file(s) before proposing changes. Do not guess at existing signatures or table schemas.
- **Proto changes are API changes.** Editing `.proto` files affects both ControlPlane and Agent. Note the downstream impact explicitly before making changes.
- **EF Core migrations are append-only.** Never edit existing migration files. Add a new migration for any schema change.
- **NativeAOT constraints apply to the CLI.** Avoid reflection, `dynamic`, and runtime code generation in `Clustral.Cli` and anything in `Clustral.Sdk` that the CLI uses. Prefer source generators.
- **gRPC stubs are generated.** Files under `*/Generated/` must not be edited manually.
- **OIDC provider config** — the ControlPlane reads OIDC settings from the `Oidc` section in appsettings.json (falls back to `Keycloak` for backward compatibility). If using Keycloak, the realm export in `infra/keycloak/` must be updated when adding new OAuth scopes or clients.
- **Web state management**: local/ephemeral UI state → React `useState`; server state → TanStack Query; cross-component shared state → Zustand. Do not reach for Zustand for data that TanStack Query already owns.
- **Helm chart changes** in `infra/helm/` must keep `values.yaml` as the source of truth; do not hardcode values in templates.
- **Security-sensitive paths** (token handling in `TokenCache`, tunnel auth in `TunnelService`) require extra care. Flag any change that touches these for explicit review rather than silently implementing.
- **Keep PRs focused.** Proto change, migration, and implementation should be reviewable together but should be called out as distinct layers in the PR description.
- **Domain-Driven Design** — the ControlPlane uses DDD with aggregate roots, domain services, specifications, repositories, and domain events. `AccessRequest` is an aggregate root with state transition methods. Domain services include `UserSyncService`, `ProxyAuthService`, and `ImpersonationResolver`. Handlers are thin orchestrators — business logic lives in the domain.
- **Kubectl proxy** uses domain services (`ProxyAuthService`, `ImpersonationResolver`) for auth and role resolution. Configurable tunnel timeout and per-credential token bucket rate limiting via `Proxy` section in appsettings.
- **CQS (Command-Query Separation)** — commands (writes) and queries (reads) are explicitly separated via `ICommand<T>` and `IQuery<T>` marker interfaces, and live in separate `Commands/` and `Queries/` subfolders under each feature. Validation only runs for commands. All queries return `Result<T>`.
- **Vertical slicing architecture** in ControlPlane: every new feature goes in `Features/<FeatureName>/Commands/` and `Features/<FeatureName>/Queries/` with handler + validator. Controllers are thin MediatR dispatchers — no business logic.
- **Use `Result<T>` in handlers** instead of throwing exceptions. Use `ResultErrors.*` catalog for consistent error codes across the codebase. Controllers map results via `ToActionResult()`.
- **FluentValidation everywhere.** Every new feature in the ControlPlane or CLI **must** use FluentValidation for input validation. In the ControlPlane, validators run automatically via the `ValidationBehavior` MediatR pipeline. In the CLI, validators are instantiated directly via `ValidationHelper.Validate()`. Do not use `[Required]` or other data annotation attributes on DTOs. Do not use manual `string.IsNullOrWhiteSpace` checks for validation — use a FluentValidation validator instead.
- **FluentAssertions everywhere.** All tests across ControlPlane, CLI, and SDK **must** use FluentAssertions (`.Should().Be(...)` style assertions). Do not use `Assert.Equal` / `Assert.True` — use `.Should().Be()` / `.Should().BeTrue()` instead.
- **CLI input validation** uses `Validation/` folder with input records, validators, and `ValidationHelper`. Validation errors are displayed as yellow-bordered cards via `CliErrors.WriteValidationErrors()`.
- **CLI error display** uses `CliErrors.*` card-style display for user-friendly error output.
- **Command aliases**: all listing commands support both `list` and `ls` aliases.
- **xUnit test output**: use `ITestOutputHelper` in xUnit tests, not `Console.WriteLine`.
- **Integration tests need Docker** running for Testcontainers.
- **Every new feature must include tests.** Write both unit tests and integration tests for ControlPlane, CLI, and SDK changes. Integration tests use `WebApplicationFactory` + Testcontainers MongoDB. Frontend (Web UI) tests are not yet required but will be added later.
