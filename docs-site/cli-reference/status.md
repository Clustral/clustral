---
description: One-screen snapshot of your session, kubeconfig contexts, active JIT grants, and ControlPlane reachability.
---

# `clustral status`

Print a compact overview of session validity, Clustral kubeconfig contexts, active access grants, and ControlPlane reachability. Designed to be the first command to run when `kubectl` misbehaves.

## Synopsis

```
clustral status [--json] [--insecure]
```

## Description

`clustral status` collects state from three places:

1. **Local** — active profile, active account, cached OIDC token file, token expiry decoded from the JWT.
2. **Kubeconfig** — every context named `clustral-*` and whether it still has a token.
3. **Remote** (only if a valid session and a configured ControlPlane URL exist) — ControlPlane version and your currently-active JIT grants via `GET /api/v1/users/me`.

Remote calls are best-effort. If the ControlPlane is unreachable or your token is expired, the local sections still render and the remote section shows `Unreachable`.

## Options

| Flag | Description | Default |
|---|---|---|
| `--json` | Emit machine-readable JSON. Always returns exit 0 when parsing succeeds. | `false` |
| `--insecure` | Skip TLS verification on ControlPlane probe. | `false` |

## Examples

### Normal output

```bash
$ clustral status

Session  (profile: prod)
  Logged in as  alice@example.com
  Account       alice@example.com (2 accounts available)
  Valid until   2026-04-14 18:42:03 +02:00  [valid for 7h53m]

Clusters (kubeconfig)
  ● clustral-prod-us-east
  ● clustral-dev-sandbox
  ○ clustral-prod-eu-west

Active Grants
  prod-us-east → sre  [JIT 2h14m remaining]

ControlPlane
  ● Online  https://controlplane.clustral.example  (v0.4.1)
```

A filled dot (`●`) next to a cluster context means the user entry still has a token; a hollow dot (`○`) means the token was wiped (e.g., by `clustral kube logout`) but the context is still in `~/.kube/config`.

### Machine-readable

```bash
$ clustral status --json | jq '.grants'
[
  {
    "clusterName": "prod-us-east",
    "roleName": "sre",
    "expiresAt": "2026-04-14T16:42:03Z"
  }
]

$ clustral status --json | jq '.session.valid'
true
```

### Shell prompt integration

```bash
# zsh prompt fragment — shows active cluster and grant expiry if any.
clustral_prompt() {
  clustral status --json 2>/dev/null \
    | jq -r '.grants[0] | "[\(.clusterName):\(.roleName)]"' 2>/dev/null
}
PROMPT='$(clustral_prompt) %~ %# '
```

### Session expired

```bash
$ clustral status

Session  (profile: prod)
  Status  Not logged in

Clusters (kubeconfig)
  ○ clustral-prod-us-east

ControlPlane
  ● Unreachable  https://controlplane.clustral.example
```

The `Unreachable` here is a consequence of `Not logged in` — without a valid token the CLI does not probe the ControlPlane.

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Status shows `Not logged in` but you just logged in. | Active account is a different one than the login just stored. | `clustral accounts list` and `clustral accounts use <email>`. |
| Cluster appears in kubeconfig but `kubectl` returns 502. | Agent is disconnected from ControlPlane. | Check the agent pod: `kubectl -n clustral logs deploy/clustral-agent`. Escalate to the operator if it keeps reconnecting. |
| `ControlPlane ● Unreachable` but network is fine. | TLS verification fails (self-signed cert), or the ControlPlane URL in config is wrong. | Pass `--insecure` for local dev, or fix `controlPlaneUrl` via `clustral login <new-url>`. |
| JSON output is missing the `grants` field. | Your token is valid but you have no active grants. | Normal. Filter with `jq '.grants // []'` to handle safely. |
| Exit code 2 in scripts. | Non-JSON mode signaled a remote-health failure. | Use `--json` for scripting — JSON mode returns 0 when parseable. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Status rendered. With `--json`, always 0 on successful emit. |
| 1 | Generic error (I/O failure reading local state). |

## See also

- [`clustral doctor`](README.md) — deeper, sequential connectivity diagnostics.
- [`clustral config`](config.md) — the same local state in more detail.
- [`clustral login`](login.md) — run this if `status` shows `Not logged in`.
- [Authentication Flows](../architecture/authentication-flows.md) — where session and grants come from.
