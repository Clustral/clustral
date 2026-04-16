---
description: Manage named configuration profiles for switching between environments (dev, staging, prod).
---

# `clustral profiles`

Create, list, switch, and delete configuration profiles. Each profile has its own ControlPlane URL, OIDC settings, and cached tokens.

## Synopsis

```
clustral profiles list                   # alias: ls
clustral profiles current
clustral profiles create <name>
clustral profiles use    <name>
clustral profiles delete <name>
```

## Description

A profile is a sibling directory under `~/.clustral/profiles/<name>/` with its own `config.json`, `token`, and `accounts/`. The active profile is recorded in `~/.clustral/active-profile`. When a profile is active, every CLI command reads configuration and tokens from the profile directory instead of the default root.

The `default` profile is special — it lives directly under `~/.clustral/` (not under `profiles/default/`) and cannot be deleted. `clustral profiles use default` clears the active-profile pointer, restoring the default behavior.

Use cases:

- One laptop, multiple deployments (`dev`, `staging`, `prod`).
- Isolating test identities from a production login.
- Scripted environment switching: `CLUSTRAL_PROFILE=staging clustral clusters list` overrides for a single command (see [`clustral config`](config.md) for the environment-variable contract).

## Subcommands

### `clustral profiles list` (alias `ls`)

List all profiles with their ControlPlane URL, login status, and account count.

### `clustral profiles current`

Print the active profile name, or `(using default config)` if none is active. Supports `--json`.

### `clustral profiles create <name>`

Create an empty profile directory with a stub `config.json`. Fails if the profile already exists.

### `clustral profiles use <name>`

Switch the active profile. Use `default` to clear the active-profile pointer. Fails if the named profile does not exist — create it first.

### `clustral profiles delete <name>`

Remove the profile directory and everything under it (config, token, accounts). Refuses to delete `default`. If deleting the currently-active profile, falls back to `default`.

## Examples

### Inspect profiles

```bash
$ clustral profiles list
   Profile      ControlPlane URL                             Status      Accounts
 ○ default      https://controlplane.clustral.example        logged in   1
 ● staging      https://controlplane.staging.clustral.ex     active      1
 ○ prod         https://controlplane.clustral.example        logged in   2
```

### Switch profiles

```bash
$ clustral profiles use prod
✓ Switched to profile prod
  ControlPlane  https://controlplane.clustral.example

$ clustral profiles current
● prod
```

### Create a new one

```bash
$ clustral profiles create dev
✓ Created profile dev
  Login with: clustral profiles use dev && clustral login <url>

$ clustral profiles use dev
✓ Switched to profile dev

$ clustral login https://controlplane.dev.clustral.example
```

### One-off override without switching

```bash
$ CLUSTRAL_PROFILE=staging clustral clusters list
Cluster           Status       ...
staging-eu-west   ● Connected  ...
```

The active profile on disk is unchanged — only the single command sees `staging`.

### Delete a profile

```bash
$ clustral profiles delete dev
✗ Deleted profile dev
```

### JSON listing

```bash
$ clustral profiles list --json | jq '.[] | select(.active)'
{"name":"staging","active":true,"controlPlaneUrl":"https://controlplane.staging.clustral.example"}
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Profile '<name>' does not exist.` on `use` | You skipped the `create` step. | `clustral profiles create <name>` first. |
| `The default profile cannot be deleted.` | You tried `clustral profiles delete default`. | Delete a named profile instead; switch to `default` with `clustral profiles use default`. |
| A profile exists but `config.json` is empty | Created via `create` but never populated by `login`. | `clustral profiles use <name> && clustral login <url>`. |
| Environment variable `CLUSTRAL_PROFILE` is set but ignored | The variable is shadowed by `clustral profiles use` writing the active-profile file. | Unset the env var, or use `clustral profiles use default` to rely on env-only overrides. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Subcommand succeeded. |
| 1 | Profile missing, already exists, or deletion refused (e.g., `default`). |

## See also

- [`clustral login`](login.md) — populate a freshly created profile.
- [`clustral accounts`](accounts.md) — per-profile identity switching.
- [`clustral config`](config.md) — see the full on-disk layout for profiles.
