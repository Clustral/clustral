---
description: List users known to the ControlPlane — primarily to discover reviewers for access requests.
---

# `clustral users`

List users that have authenticated against the ControlPlane at least once.

## Synopsis

```
clustral users list [--insecure] [--json]
clustral users ls   [--insecure] [--json]
```

`list` and `ls` are aliases.

## Description

Users are synthesized lazily in the ControlPlane the first time someone logs in — there is no user-provisioning step. `clustral users list` calls `GET /api/v1/users` and renders the result.

The most common use is picking a reviewer email for `clustral access request --reviewer <email>`. Cross-reference this list with `clustral roles list` to see who has approval authority in your deployment.

User-mutation commands (create, delete, role assignment) are not exposed on the CLI. Managed-identity provisioning is expected to happen upstream in your OIDC provider.

## Subcommands

### `clustral users list` (alias `ls`)

List all users the ControlPlane has seen.

| Flag | Description | Default |
|---|---|---|
| `--insecure` | Skip TLS verification. | `false` |
| `--json` | Emit machine-readable JSON. | `false` |

## Examples

### Default output

```bash
$ clustral users list
Email                      Display Name          Last Seen
alice@example.com          Alice Example         2m ago
bob@example.com            Bob Example           14m ago
carol@example.com          Carol Example         3h ago
dev-ci@example.com         CI Automation         —
```

### JSON

```bash
$ clustral users list --json | jq '.users[] | select(.email | test("@example.com$"))'
```

### Find the reviewer to tag

```bash
$ clustral users list | grep sre
# ...then use the email in the access request:
$ clustral access request --cluster prod-us-east --role sre \
    --duration 2H --reviewer bob@example.com
```

### Export to CSV

```bash
$ clustral users list --json \
  | jq -r '["email","displayName","lastSeen"], (.users[] | [.email, .displayName, .lastSeenAt]) | @csv' \
  > users.csv
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `No users found.` | Nobody has logged in yet; the database is empty. | Run `clustral login` from any account — the first login provisions the user. |
| A known user is missing | They have never authenticated against this ControlPlane, or the deployment was recently reset. | Have them log in once; the next `users list` will include them. |
| `Last Seen —` for an active user | Either they have only authenticated via a kubeconfig JWT (no OIDC login), or the field is not yet populated. | Expected for service accounts and CI identities. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Query succeeded (including empty result set). |
| 1 | Not logged in, or HTTP error from the ControlPlane. |

## See also

- [`clustral roles`](roles.md) — which roles exist and what they impersonate.
- [`clustral access`](access.md) — use a user's email as `--reviewer`.
- [`clustral whoami`](whoami.md) — check your own identity.
