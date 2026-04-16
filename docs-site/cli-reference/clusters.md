---
description: List Clustral-registered clusters with connection status, agent version, and Kubernetes version.
---

# `clustral clusters`

List Clustral-registered clusters and inspect their connection status from the ControlPlane.

## Synopsis

```
clustral clusters list [--status <state>] [--insecure] [--json]
clustral clusters ls   [--status <state>] [--insecure] [--json]
```

`list` and `ls` are aliases for the same command.

## Description

Calls `GET /api/v1/clusters` on the ControlPlane and renders the response as a table (default) or JSON. Each row shows:

- **Cluster** — the registered name.
- **ID** — the cluster GUID (what `kubeconfig` `server:` URLs embed).
- **Status** — one of `Pending`, `Connected`, `Disconnected`, driven by agent heartbeats.
- **Agent Version** — the Go agent's reported version from its `AgentHello` handshake.
- **K8s Version** — Kubernetes API server version discovered once at agent startup.
- **Last Seen** — relative time since the last heartbeat.
- **Labels** — arbitrary `key=value` pairs attached to the cluster.

Administrative mutations (register, deregister, update) currently happen through the API or Web UI — the CLI only exposes `list` today.

## Subcommands

### `clustral clusters list` (alias `ls`)

List registered clusters, optionally filtered by status.

| Flag | Description | Default |
|---|---|---|
| `--status <state>` | Filter by status: `Pending`, `Connected`, `Disconnected`. | all |
| `--insecure` | Skip TLS verification. | `false` |
| `--json` | Emit machine-readable JSON. | `false` |

## Examples

### All clusters

```bash
$ clustral clusters list
Cluster           ID                                    Status           Agent Version  K8s Version  Last Seen  Labels
prod-us-east      7a4c5b12-6f3d-4e8a-b2c1-4d5e6f7a8b9c  ● Connected      v0.4.1         v1.29.4      14s ago    env=prod, region=us-east
prod-eu-west      8b5d6c23-7e4a-5f9b-c3d2-5e6f7a8b9c0d  ● Connected      v0.4.1         v1.29.4      22s ago    env=prod, region=eu-west
dev-sandbox       9c6e7d34-8f5b-6a0c-d4e3-6f7a8b9c0d1e  ● Disconnected   v0.3.2         v1.28.7      4h ago     env=dev
staging-eu        0d7f8e45-9a6c-7b1d-e5f4-7a8b9c0d1e2f  ● Pending        —              —            —          env=staging
```

### Just connected clusters

```bash
$ clustral clusters list --status Connected
Cluster           ID                                    Status         Agent Version  K8s Version  Last Seen  Labels
prod-us-east      7a4c5b12-6f3d-4e8a-b2c1-4d5e6f7a8b9c  ● Connected    v0.4.1         v1.29.4      14s ago    env=prod, region=us-east
prod-eu-west      8b5d6c23-7e4a-5f9b-c3d2-5e6f7a8b9c0d  ● Connected    v0.4.1         v1.29.4      22s ago    env=prod, region=eu-west
```

### Only show disconnected — alert-friendly

```bash
$ clustral clusters list --status Disconnected --json \
  | jq -r '.clusters[] | "\(.name)\t\(.lastSeenAt // "never")"'
dev-sandbox    2026-04-14T08:42:03Z
```

### Script-friendly: get a cluster's ID by name

```bash
$ CLUSTER_ID=$(clustral clusters list --json \
    | jq -r '.clusters[] | select(.name=="prod-us-east") | .id')
$ echo "$CLUSTER_ID"
7a4c5b12-6f3d-4e8a-b2c1-4d5e6f7a8b9c
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `No clusters found.` | No agent has registered yet. | Deploy the agent Helm chart; see [Agent Deployment](../agent-deployment/README.md). |
| A cluster shows `● Pending` for a long time | Agent registered but has not completed its first `AgentHello`. | `kubectl -n clustral logs deploy/clustral-agent` to inspect the handshake. |
| A cluster shows `● Disconnected` | Heartbeat has not arrived within the grace period. | Check agent pod status; verify outbound reachability to the ControlPlane's gRPC port. |
| `v0.3.2` agent on a ControlPlane running `v0.4.1` | Agent needs upgrading. | Upgrade the agent Helm release to match the ControlPlane; mismatches are logged as warnings server-side. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Query succeeded (including empty result set). |
| 1 | Not logged in, or HTTP error from the ControlPlane. |

## See also

- [`clustral kube login`](kube-login.md) — use a cluster from the list.
- [`clustral kube list`](kube-list.md) — the same list, cross-referenced with your kubeconfig.
- [Agent Deployment](../agent-deployment/README.md) — how clusters get registered.
- [Tunnel Lifecycle](../architecture/tunnel-lifecycle.md) — what `Connected` means behind the scenes.
