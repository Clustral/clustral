---
description: Show the current user, active profile, and session validity without making any network calls.
---

# `clustral whoami`

Print who you are signed in as, which profile is active, and how long the cached OIDC token remains valid. Entirely local — no calls to the ControlPlane.

## Synopsis

```
clustral whoami [--json]
```

## Description

`clustral whoami` reads the cached JWT for the active account of the active profile, decodes the `email` (or `preferred_username`, or `sub`) and `exp` claims in memory, and prints a one-line status.

If no token is cached the command prints `Not logged in`. If a token is present but expired it prints the email alongside `(expired)`. The command never reaches out to the ControlPlane or to the OIDC provider — use it inside tight loops, shell prompts, and pre-commit hooks without worrying about latency.

## Options

No command-specific flags. The global `--json` flag (see [Overview](README.md)) switches to machine-readable output.

| Flag | Description | Default |
|---|---|---|
| `--json` | Emit machine-readable JSON. | `false` |

## Examples

### Logged in

```bash
$ clustral whoami
● alice@example.com (7h53m remaining)
```

### With a non-default profile

```bash
$ clustral whoami
● alice@example.com (7h53m remaining) (profile: staging)
```

### Token expired

```bash
$ clustral whoami
● alice@example.com (expired)
```

### Not logged in

```bash
$ clustral whoami
○ Not logged in
```

### Machine-readable

```bash
$ clustral whoami --json
{"loggedIn":true,"email":"alice@example.com","profile":"default","valid":true,"expiresAt":"2026-04-14T16:42:03.0000000+00:00"}

$ clustral whoami --json | jq -r '.email'
alice@example.com

$ clustral whoami --json | jq -r 'if .valid then "ok" else "needs-login" end'
ok
```

### Shell prompt fragment

```bash
# bash prompt — shows active account and remaining time.
_clustral_prompt() {
  clustral whoami --json 2>/dev/null \
    | jq -r 'if .valid then "[\(.email)]" else "" end' 2>/dev/null
}
PS1='$(_clustral_prompt) \w \$ '
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Output rendered. Always 0, even when not logged in. |
| 1 | Generic I/O error reading the token file. |

## See also

- [`clustral login`](login.md) — run this if `whoami` shows `Not logged in`.
- [`clustral status`](status.md) — the same identity plus kubeconfig and ControlPlane health.
- [`clustral accounts`](accounts.md) — switch between cached accounts.
- [`clustral config`](config.md) — inspect where the token file lives on disk.
