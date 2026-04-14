# Clustral Documentation

Clustral is an open-source Kubernetes access proxy — a Teleport alternative built on .NET, Go, and React. It lets users authenticate via any OIDC provider (Keycloak, Auth0, Okta, Azure AD), then transparently proxies `kubectl` traffic through a control plane to registered cluster agents.

## Key features

- **Zero-trust access** — OIDC authentication + role-based authorization per cluster
- **No inbound firewall rules** — agents connect outbound via gRPC tunnel
- **Just-in-time access** — request, approve, and revoke cluster access with time-limited grants
- **Audit trail** — every proxy request and access change is logged to a dedicated audit service
- **Self-service CLI** — `clustral login` / `clustral kube login` for seamless kubectl integration

## Quick links

| Section | What you'll find |
|---|---|
| [Getting Started](getting-started/README.md) | Set up Clustral for local dev or on-prem deployment |
| [Architecture](architecture/README.md) | Component diagrams, auth flows, tunnel lifecycle |
| [CLI Reference](cli-reference/README.md) | Every `clustral` command documented |
| [Error Reference](errors/README.md) | Machine-readable error codes with causes and fixes |
| [Agent Deployment](agent-deployment/README.md) | Deploy the Go agent to your Kubernetes clusters |
| [Security Model](security-model/README.md) | Threat model, mTLS, JWT lifecycle, audit |
