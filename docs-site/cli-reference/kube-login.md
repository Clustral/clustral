---
description: Exchange the cached OIDC token for a short-lived kubeconfig credential that routes kubectl through the Clustral proxy.
---

# `clustral kube login`

Issue a short-lived kubeconfig credential for a Clustral-registered cluster and write it into `~/.kube/config` as a new context.

## Synopsis

```
clustral kube login <cluster> [options]
```

`<cluster>` is either the cluster name (as shown by `clustral clusters list`) or its GUID.

## Description

The CLI reads your cached OIDC token, `POST`s to the ControlPlane `/api/v1/auth/kubeconfig-credential` endpoint, and receives an ES256 JWT bound to `(user, cluster)` with a server-capped TTL. It then upserts three entries in `~/.kube/config` — a `cluster`, a `user`, and a `context`, all named `clustral-<cluster>` by default — and (unless `--no-set-context` is passed) sets `current-context` to the new entry.

The kubeconfig `server` URL points back at the API Gateway's proxy route (`/api/proxy/<cluster-id>`). All subsequent `kubectl` traffic is authenticated by the JWT, validated by the gateway, forwarded over the ControlPlane ↔ agent tunnel, and impersonated inside the target cluster by the role granted to you.

Run `clustral kube login <cluster>` again at any time to rotate the JWT — the CLI overwrites the existing entry in place.

## Options

| Flag | Description | Default |
|---|---|---|
| `--context-name <name>` | Name for the kubeconfig context, cluster, and user entries. | `clustral-<cluster>` |
| `--ttl <duration>` | Requested credential lifetime. Accepts shorthand (`8H`, `30M`, `1D`) or ISO 8601 (`PT8H`). Server caps at `Credential:MaxKubeconfigCredentialTtl` (default 8h). | server default |
| `--no-set-context` | Do not update `current-context` after writing the entry. | `false` |
| `--insecure` | Skip TLS verification on ControlPlane calls (local dev only). | `false` |
| `--debug` | Verbose output: cluster resolution, credential issuance, kubeconfig write. | `false` |

## Examples

### Issue a credential and point kubectl at it

```bash
$ clustral kube login prod-us-east
✓ Kubeconfig updated  (profile: default)
  Context   clustral-prod-us-east
  Server    https://controlplane.clustral.example/api/proxy/7a4c…
  Expires   2026-04-15 02:41:12 +02:00
  Active    current-context set

$ kubectl config current-context
clustral-prod-us-east

$ kubectl get pods -n payments
NAME                       READY   STATUS    RESTARTS   AGE
checkout-7d9f6b8c5-h2jvt   1/1     Running   0          3h12m
checkout-7d9f6b8c5-r4k9p   1/1     Running   0          3h12m
```

### Shorter credential lifetime

```bash
$ clustral kube login prod-us-east --ttl 2H
✓ Kubeconfig updated
  Context   clustral-prod-us-east
  Expires   2026-04-14 20:41:12 +02:00
```

### Custom context name, keep existing `current-context`

```bash
$ clustral kube login prod-us-east --context-name prod --no-set-context

$ kubectl --context prod get nodes
NAME              STATUS   ROLES    AGE    VERSION
ip-10-0-1-42      Ready    <none>   182d   v1.29.4
ip-10-0-2-17      Ready    <none>   182d   v1.29.4
```

### Refresh a near-expired credential

```bash
$ clustral kube login prod-us-east
✓ Kubeconfig updated
  Context   clustral-prod-us-east
  Expires   2026-04-15 02:58:04 +02:00
```

Re-running overwrites the JWT in-place. To also revoke the previous JWT on the server, run `clustral kube logout prod-us-east` before logging in again.

### How it shows up in `~/.kube/config`

```yaml
contexts:
- name: clustral-prod-us-east
  context:
    cluster: clustral-prod-us-east
    user: clustral-prod-us-east
users:
- name: clustral-prod-us-east
  user:
    token: eyJhbGciOiJFUzI1NiIs...  # kubeconfig JWT, valid 8h
clusters:
- name: clustral-prod-us-east
  cluster:
    server: https://controlplane.clustral.example/api/proxy/7a4c5b12-...
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `error: Forbidden: no active role assignment on cluster prod-us-east` | You have no static assignment or active JIT grant on this cluster. | `clustral access request --cluster prod-us-east --role <role>`, wait for approval, retry. |
| `error: credential expired` mid-kubectl-session | JWT lifetime elapsed. | `clustral kube login <cluster>` to rotate. Raise `--ttl` or have an operator increase `Credential:MaxKubeconfigCredentialTtl`. |
| `cluster not found` | Cluster name typo, or agent never registered. | `clustral clusters list` to see what's visible; check agent Helm release and `clustral-agent` Deployment logs. |
| `error: 502 Bad Gateway` from kubectl | Agent is not currently connected to ControlPlane. | Check agent pod (`kubectl -n clustral logs deploy/clustral-agent`) and the tunnel on the server side. |
| `x509: certificate signed by unknown authority` | Your laptop does not trust the ControlPlane TLS certificate. | Add the CA to your system trust store, or pass `--insecure` for local dev only. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Kubeconfig updated. |
| 1 | Generic error (not logged in, cluster not found, credential issuance failed, kubeconfig write failed). |

## See also

- [`clustral login`](login.md) — prerequisite: obtain the OIDC token.
- [`clustral access`](access.md) — request the role you need first.
- [`NO_ROLE_ASSIGNMENT`](../errors/NO_ROLE_ASSIGNMENT.md) — when the cluster refuses authorization.
- [Authentication Flows](../architecture/authentication-flows.md) — how the kubeconfig JWT is signed and validated.
