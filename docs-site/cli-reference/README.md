---
description: Reference for every `clustral` command — authentication, kubeconfig issuance, JIT access requests, configuration, and diagnostics.
---

# CLI Reference

The `clustral` CLI provides commands for authentication, kubeconfig management, access requests, and cluster administration. Every command below is documented on its own page with options, examples, exit codes, and troubleshooting.

## Global flags

These flags are accepted by every command, not listed on each page:

| Flag | Description |
|---|---|
| `--debug` | Verbose output — PKCE values, HTTP request/response traces, cluster resolution, kubeconfig writes. Use with `CLUSTRAL_DEBUG=1` for persistent tracing. |
| `-o`, `--output <format>` | Output format: `table` (default) or `json`. |
| `--no-color` | Disable ANSI color output. |
| `--insecure` | Skip TLS verification on ControlPlane and OIDC calls. Local dev only — never use in production. |
| `--help` | Show help for a command. |

Environment variable overrides used across the CLI:

| Variable | Effect |
|---|---|
| `CLUSTRAL_PROFILE` | Override the active profile for a single command. |
| `CLUSTRAL_SERVER` | Override the ControlPlane URL for a single command. |
| `CLUSTRAL_CONFIG_DIR` | Use a different root instead of `~/.clustral/`. |
| `KUBECONFIG` | Standard kubectl variable; honored by `clustral kube login`. |

## Commands by purpose

### Authentication

| Command | What it does |
|---|---|
| [`clustral login`](login.md) | Run the OIDC PKCE flow and cache the access token. |
| [`clustral logout`](logout.md) | Sign out — revoke credentials and remove kubeconfig contexts. |
| [`clustral whoami`](whoami.md) | Quick local identity check from the cached JWT. |

### kubectl integration

| Command | What it does |
|---|---|
| [`clustral kube login`](kube-login.md) | Issue a short-lived kubeconfig credential for a cluster. |
| [`clustral kube logout`](kube-logout.md) | Disconnect from a single cluster. |
| [`clustral kube list`](kube-list.md) | List clusters with the current kubeconfig context highlighted. |

### Access management

| Command | What it does |
|---|---|
| [`clustral access`](access.md) | Request, approve, deny, and revoke JIT access grants. |
| [`clustral clusters`](clusters.md) | List registered clusters. |
| [`clustral roles`](roles.md) | List roles and their Kubernetes groups. |
| [`clustral users`](users.md) | List users known to the ControlPlane. |

### Inspection

| Command | What it does |
|---|---|
| [`clustral status`](status.md) | One-screen session, kubeconfig, and grant overview. |
| [`clustral doctor`](doctor.md) | Sequential connectivity diagnostics. |
| [`clustral audit`](audit.md) | Query audit events from the AuditService. |

### Configuration

| Command | What it does |
|---|---|
| [`clustral config`](config.md) | Inspect and reset local CLI state. |
| [`clustral profiles`](profiles.md) | Manage named configuration profiles. |
| [`clustral accounts`](accounts.md) | Switch between cached OIDC identities. |

### Utilities

| Command | What it does |
|---|---|
| [`clustral version`](version.md) | Print CLI and ControlPlane versions. |
| [`clustral update`](update.md) | Self-update from GitHub Releases. |
| [`clustral completion`](completion.md) | Generate shell completion scripts. |

## See also

- [Authentication Flows](../architecture/authentication-flows.md) — what happens under the hood during `login` and `kube login`.
- [Error Reference](../errors/README.md) — machine-readable error codes the CLI may echo from the server.
- [Contributing to the docs](../CONTRIBUTING.md) — if you add a new command, add a page next to it.
