---
description: List Kubernetes clusters registered with Clustral and highlight the one your current kubeconfig context points at.
---

# `clustral kube list`

List Clustral-registered clusters with connection status, and mark which one your `current-context` in `~/.kube/config` is pointing at.

## Synopsis

```
clustral kube list [--insecure] [--json]
clustral kube ls   [--insecure] [--json]
```

`list` and `ls` are aliases.

## Description

`clustral kube list` is tuned for the day-to-day `kubectl` workflow: "which clusters can I reach, and which one am I talking to right now?" It calls `GET /api/v1/clusters` on the ControlPlane (like `clustral clusters list`) and reads `~/.kube/config` locally to resolve `current-context`. If the current context is a `clustral-*` one, the matching row is marked with a green arrow and bolded cluster name.

Columns are the same as `clustral clusters list`, plus a leading pointer column. Use `clustral clusters list` when you want the administrative view (no pointer) and `clustral kube list` when you want the user-of-kubectl view.

## Options

| Flag | Description | Default |
|---|---|---|
| `--insecure` | Skip TLS verification. | `false` |
| `--json` | Emit machine-readable JSON (no pointer column — the JSON is identical to `clustral clusters list --json`). | `false` |

## Examples

### Default output

```bash
$ clustral kube list
    Cluster           ID                                    Status           Agent Version  K8s Version  Labels
  ▸ prod-us-east      7a4c5b12-6f3d-4e8a-b2c1-4d5e6f7a8b9c  ● Connected      v0.4.1         v1.29.4      env=prod, region=us-east
    prod-eu-west      8b5d6c23-7e4a-5f9b-c3d2-5e6f7a8b9c0d  ● Connected      v0.4.1         v1.29.4      env=prod, region=eu-west
    dev-sandbox       9c6e7d34-8f5b-6a0c-d4e3-6f7a8b9c0d1e  ● Disconnected   v0.3.2         v1.28.7      env=dev
```

The green arrow (`▸`) marks the cluster your `current-context` points at. In this example, `kubectl` is currently talking to `prod-us-east` via the Clustral proxy.

### No `current-context` set — no arrow

```bash
$ clustral kube list
    Cluster           ID                                    Status           Agent Version  K8s Version  Labels
    prod-us-east      7a4c5b12-...                          ● Connected      v0.4.1         v1.29.4      env=prod, region=us-east
    prod-eu-west      8b5d6c23-...                          ● Connected      v0.4.1         v1.29.4      env=prod, region=eu-west
```

### Pick a cluster to connect to

```bash
$ clustral kube list
    Cluster          ...
    prod-us-east     ...
    prod-eu-west     ...
    dev-sandbox      ...

$ clustral kube login dev-sandbox
✓ Kubeconfig updated
  Context   clustral-dev-sandbox
  Expires   2026-04-15 02:41:12 +02:00
  Active    current-context set

$ clustral kube list
    Cluster          ...
    prod-us-east     ...
    prod-eu-west     ...
  ▸ dev-sandbox      ...
```

### Machine-readable

```bash
$ clustral kube list --json | jq '.clusters[] | select(.status=="Connected") | .name'
"prod-us-east"
"prod-eu-west"
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `No Kubernetes clusters found.` | No agent has registered with the ControlPlane yet. | Deploy the agent Helm chart; see [Agent Deployment](../agent-deployment/README.md). |
| Arrow never appears even after `clustral kube login` | `$KUBECONFIG` points somewhere other than `~/.kube/config`, or `current-context` was cleared manually. | `kubectl config current-context` to verify; re-run `clustral kube login <cluster>` without `--no-set-context`. |
| Cluster shows `● Disconnected` | Agent heartbeat has not arrived within the grace window. | `kubectl -n clustral logs deploy/clustral-agent` on the target cluster. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Query succeeded (including empty result set). |
| 1 | Not logged in, or HTTP error from the ControlPlane. |

## See also

- [`clustral kube login`](kube-login.md) — connect to the cluster picked from the list.
- [`clustral kube logout`](kube-logout.md) — disconnect from one cluster.
- [`clustral clusters`](clusters.md) — administrative view without kubeconfig cross-referencing.
- [`clustral status`](status.md) — the same view plus session and grant details.
