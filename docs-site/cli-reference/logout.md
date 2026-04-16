---
description: Sign out — revoke Clustral kubeconfig credentials, remove `clustral-*` contexts from kubeconfig, and clear the cached OIDC token.
---

# `clustral logout`

Sign out of Clustral. Clears the cached OIDC token, removes every `clustral-*` context from `~/.kube/config`, and best-effort revokes each kubeconfig JWT on the ControlPlane.

## Synopsis

```
clustral logout [--insecure]
```

## Description

Logout runs in two phases:

1. **Local, synchronous.** Removes every `clustral-*` cluster/user/context entry from `~/.kube/config`. Clears the JWT at `~/.clustral/token`. Deletes the per-account token files under `accounts/` for the active profile and clears the active-account pointer. This phase always succeeds — it never requires the network.
2. **Remote, best-effort.** For each kubeconfig JWT it found in step 1, the CLI POSTs to `/api/v1/auth/revoke-by-token` on the ControlPlane with a 5s spinner timeout. Failures print a warning but do not fail the command — local state is already clean by that point.

A `clustral-*` kubeconfig context with no corresponding server-side revocation will expire naturally at its JWT `exp`. If you need immediate revocation and the network is down, run `clustral logout` again once connectivity is restored, or revoke via `/api/v1/auth/revoke-by-token` from another machine.

## Options

| Flag | Description | Default |
|---|---|---|
| `--insecure` | Skip TLS verification on ControlPlane calls. | `false` |

## Examples

### Standard logout

```bash
$ clustral logout
  ✗ Removed kubeconfig context: clustral-prod-us-east
  ✗ Removed kubeconfig context: clustral-dev-sandbox
  ✗ Cleared 2 account token(s)

✓ Logged out locally
✓ Revoked 2 credential(s).
```

### Offline — local cleanup still succeeds

```bash
$ clustral logout
  ✗ Removed kubeconfig context: clustral-prod-us-east
  ✗ Cleared 1 account token(s)

✓ Logged out locally
! ControlPlane unreachable. Remote credentials will expire naturally.
```

### Nothing to revoke

```bash
$ clustral logout
✓ Logged out locally
```

If there are no `clustral-*` contexts and no cached token, the command still succeeds and prints the local summary line.

### Verify the result

```bash
$ clustral whoami
○ Not logged in

$ kubectl config get-contexts -o name | grep '^clustral-'
(no output)
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `! ControlPlane unreachable` | DNS, TLS, or network failure during revocation. | Local state is already clean. Retry later if you need server-side revocation now. |
| `! Credential revocation returned 401` | The OIDC token expired before the revocation request completed. | Re-log in and run `clustral logout` again, or revoke from the Web UI. |
| `kubectl` still uses a Clustral context after logout | You had multiple kubeconfig files merged; `$KUBECONFIG` points elsewhere. | Run `clustral logout` with `KUBECONFIG` set to the file that actually holds the contexts, or merge kubeconfigs. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Local logout succeeded (even when remote revocation failed). |
| 1 | Generic error (e.g., kubeconfig file unreadable or unwritable). |

## See also

- [`clustral login`](login.md) — sign back in.
- [`clustral kube logout`](kube-logout.md) — sign out of a single cluster while keeping the session.
- [`clustral config`](config.md) — inspect what local state still exists.
- [`clustral status`](status.md) — verify the session is gone and no grants remain.
