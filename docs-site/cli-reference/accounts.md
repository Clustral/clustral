---
description: Manage multiple OIDC identities within a single profile — list, switch, or remove cached accounts.
---

# `clustral accounts`

Switch between multiple OIDC identities that share one profile. Each account is a separate JWT stored under `<profile>/accounts/<email>.token`.

## Synopsis

```
clustral accounts list           # alias: ls
clustral accounts use    <email>
clustral accounts remove <email>
```

## Description

A profile points at one ControlPlane. An account is a specific user identity within that profile. Multiple accounts let one operator hold — say — a developer login and an SRE login at the same ControlPlane without re-running OIDC each time.

Accounts are populated by `clustral login`: each successful OIDC flow stores the token at `<profile>/accounts/<email>.token` and sets `active-account` to that email. `clustral accounts use <email>` flips the pointer; every subsequent command reads that token. `clustral accounts remove <email>` deletes the token file.

Token expiry is decoded locally from the JWT — each list entry shows how much time is left on each session independently.

## Subcommands

### `clustral accounts list` (alias `ls`)

List cached accounts with a filled dot next to the active one and a validity badge decoded from each JWT's `exp`.

### `clustral accounts use <email>`

Switch the active account. Fails if the email has no cached token — `clustral login --account <email>` first.

### `clustral accounts remove <email>`

Delete the cached token. If the account being removed is the active one, the active-account pointer is also cleared.

## Examples

### List accounts

```bash
$ clustral accounts list
  ● alice@example.com (7h53m remaining)
  ○ alice.admin@example.com (2h14m remaining)
  ○ alice.readonly@example.com (expired)
```

The filled dot marks the active account.

### Switch

```bash
$ clustral accounts use alice.admin@example.com
✓ Switched to account alice.admin@example.com

$ clustral whoami
● alice.admin@example.com (2h14m remaining)
```

### Add a second identity

```bash
$ clustral login --force --account alice.admin@example.com
# Browser opens, signs in as the admin account.
$ clustral accounts list
  ○ alice@example.com           (7h53m remaining)
  ● alice.admin@example.com     (7h58m remaining)
```

`--force` bypasses the SSO cookie so the IdP prompts you for credentials instead of silently reusing the first login.

### Remove an expired account

```bash
$ clustral accounts remove alice.readonly@example.com
✗ Removed account alice.readonly@example.com
```

### JSON

```bash
$ clustral accounts list --json | jq '.[] | select(.valid)'
{"email":"alice@example.com","active":true,"valid":true,"expiresAt":"2026-04-14T16:42:03.0000000Z"}
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Account '<email>' not found.` on `use` | No token file at `<profile>/accounts/<email>.token`. | `clustral login --account <email>` to create it. |
| `Account '<email>' not found.` on `remove` | Already removed, or path typo. | `clustral accounts list` to see what's actually cached. |
| `accounts list` is empty but `whoami` shows a session | Legacy single-token layout (`~/.clustral/token` without an `accounts/` dir). | Run `clustral login --force` once; subsequent logins populate the account store. |
| Multiple accounts show as active simultaneously | Impossible — the `active-account` file holds a single line. | `clustral config show` to confirm; if inconsistent, `clustral accounts use <email>` to rewrite the pointer. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Subcommand succeeded. |
| 1 | Account not found, or I/O error reading account files. |

## See also

- [`clustral login`](login.md) — what populates the account store.
- [`clustral profiles`](profiles.md) — the enclosing concept; one profile holds many accounts.
- [`clustral whoami`](whoami.md) — confirm which account is active after a switch.
- [`clustral config`](config.md) — inspect the on-disk layout.
