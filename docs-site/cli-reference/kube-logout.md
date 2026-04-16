---
description: Disconnect from a single cluster — revoke its kubeconfig credential and remove the kubeconfig context.
---

# `clustral kube logout`

Disconnect from one Clustral-registered cluster. Removes the `clustral-<cluster>` entry from `~/.kube/config` and best-effort revokes its kubeconfig JWT on the ControlPlane.

## Synopsis

```
clustral kube logout <cluster> [--insecure]
```

`<cluster>` is the cluster name or ID. If you pass the bare name (`prod-us-east`) the CLI prefixes `clustral-` to build the kubeconfig context name; if you pass `clustral-prod-us-east` verbatim it is used as-is.

## Description

Runs in two phases — identical to `clustral logout` but scoped to a single cluster:

1. **Local, synchronous.** Finds the `clustral-<cluster>` context in `~/.kube/config`, reads the JWT stored on its user entry, and removes the cluster/user/context entries in place.
2. **Remote, best-effort.** POSTs the JWT to `/api/v1/auth/revoke-by-token` on the ControlPlane with a 5s spinner timeout. Failures print a warning; local state is already clean.

The session token at `~/.clustral/token` is untouched — you remain logged in for the rest of your clusters.

If the context does not exist in `~/.kube/config`, the command prints a warning and exits 0 without touching anything. Use it idempotently in teardown scripts.

## Options

| Flag | Description | Default |
|---|---|---|
| `--insecure` | Skip TLS verification on ControlPlane calls. | `false` |

## Examples

### Disconnect from one cluster

```bash
$ clustral kube logout prod-us-east
  ✗ Removed kubeconfig context clustral-prod-us-east

✓ Disconnected from clustral-prod-us-east
✓ Revoked credential on ControlPlane.
```

### Pass the full context name

```bash
$ clustral kube logout clustral-prod-us-east
  ✗ Removed kubeconfig context clustral-prod-us-east
✓ Disconnected from clustral-prod-us-east
✓ Revoked credential on ControlPlane.
```

### Context doesn't exist

```bash
$ clustral kube logout prod-eu-west
! Context 'clustral-prod-eu-west' not found in kubeconfig. Nothing to do.
```

### Offline — local cleanup still succeeds

```bash
$ clustral kube logout prod-us-east
  ✗ Removed kubeconfig context clustral-prod-us-east
✓ Disconnected from clustral-prod-us-east
! ControlPlane unreachable. Remote credentials will expire naturally.
```

### Verify

```bash
$ kubectl config get-contexts -o name | grep clustral-prod-us-east
(no output)

$ clustral status | grep prod-us-east
(no output — the entry is gone)
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Context not found` but `kubectl` still points at the cluster | `$KUBECONFIG` points to a different file than the CLI default (`~/.kube/config`). | `KUBECONFIG=/path/to/other.yaml clustral kube logout <cluster>`. |
| Revocation returns `401 Unauthorized` | Your session OIDC token expired. | Re-log in (`clustral login`) and re-run; the local context is already gone. |
| Credential appears still valid in `clustral audit` after logout | Server-side revocation failed and the JWT has not reached its `exp`. | Re-run `clustral kube logout <cluster>` or revoke via the Web UI. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Local cleanup succeeded (even when remote revocation failed, and when nothing had to be removed). |
| 1 | Generic error (e.g., kubeconfig file unreadable or unwritable). |

## See also

- [`clustral kube login`](kube-login.md) — the inverse: connect to a cluster.
- [`clustral logout`](logout.md) — sign out everywhere in one command.
- [`clustral kube list`](kube-list.md) — see which clusters are still connected.
