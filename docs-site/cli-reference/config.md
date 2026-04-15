---
description: Inspect and reset local CLI state — profiles, accounts, tokens, kubeconfig contexts, and the files behind them.
---

# `clustral config`

Show what the CLI knows about itself — config files, active profile and account, session expiry, kubeconfig contexts — and reset it to a fresh state when needed.

## Synopsis

```
clustral config                       # alias for: clustral config show
clustral config show  [--json] [--remote]
clustral config path
clustral config clean [--yes] [--dry-run]
```

## Description

All CLI state lives under `~/.clustral/`. `clustral config` is a read-only, offline introspection of that tree plus the kubeconfig contexts the CLI has written. `clustral config clean` is the "factory reset" — it removes tokens, profiles, and any `clustral-*` kubeconfig contexts.

Two concepts shape the layout:

- **Profile** — a target environment (staging, prod, dev). Each profile has its own `config.json` with server URL, OIDC authority, client ID, and scopes. Switch with `clustral profiles use <name>`.
- **Account** — a specific user identity stored within a profile. Multiple accounts let one person hold separate SRE and developer identities on the same ControlPlane. Switch with `clustral accounts use <email>`.

## File layout

```
~/.clustral/
├── config.json                         # default profile server + OIDC settings
├── token                               # legacy single-token file (fallback)
├── active-profile                      # pointer: name of the active profile
├── active-account                      # pointer: active account in the default profile
├── accounts/
│   └── alice@example.com.token         # OIDC access token for one account
└── profiles/
    ├── staging/
    │   ├── config.json
    │   ├── active-account
    │   └── accounts/
    │       └── alice@example.com.token
    └── prod/
        ├── config.json
        └── accounts/
            ├── alice@example.com.token
            └── alice.admin@example.com.token
```

## Subcommands

### `clustral config show` (default)

Print a human-readable snapshot: profile list with the active one highlighted, active account, ControlPlane URL and OIDC settings, session status and expiry, kubeconfig path and `clustral-*` contexts, per-profile file sizes, CLI version.

| Flag | Description | Default |
|---|---|---|
| `--json` | Emit machine-readable JSON. | `false` |
| `--remote` | Also call the ControlPlane for server version and profile. | `false` |

### `clustral config path`

Print only the file paths — one per line. Useful for scripting.

```
~/.clustral/config.json
~/.clustral/token
~/.kube/config
```

### `clustral config clean`

Factory-reset all CLI state. Removes `config.json`, all tokens, all profiles, all accounts, and every `clustral-*` kubeconfig context. Does not touch non-Clustral kubeconfig entries.

| Flag | Description | Default |
|---|---|---|
| `-y`, `--yes` | Skip the confirmation prompt. Required in non-interactive shells. | `false` |
| `--dry-run` | Print what would be removed and exit without deleting. | `false` |

## Environment variable overrides

| Variable | Effect |
|---|---|
| `CLUSTRAL_PROFILE` | Override the active profile for a single command. |
| `CLUSTRAL_SERVER` | Override the ControlPlane URL for a single command. |
| `CLUSTRAL_CONFIG_DIR` | Use a different root instead of `~/.clustral/`. |
| `KUBECONFIG` | Standard kubectl variable; honored by `clustral kube login` when writing entries. |

## Examples

### Snapshot current state

```bash
$ clustral config
✓ Configured  (profile: prod)

Profiles
  ● prod
  ○ staging
  Account  alice@example.com

Control plane
  URL             https://controlplane.clustral.example
  OIDC issuer     https://keycloak.example.com/realms/clustral
  OIDC client     clustral-cli
  OIDC scopes     openid email profile
  Callback port   7777
  Insecure TLS    false

Session
  Status       Logged in
  Subject      alice@example.com
  Valid until  2026-04-14 18:42:03 +02:00  [valid for 7h53m]

Kubernetes
  Kubeconfig         ~/.kube/config  (14 contexts)
  Current context    clustral-prod-us-east
  Clustral contexts  clustral-prod-us-east, clustral-dev-sandbox (2 of 14 total)

Files
  prod
    Config    ~/.clustral/profiles/prod/config.json  (412 B)
    Token     ~/.clustral/profiles/prod/token  (does not exist)
    Accounts  alice@example.com, alice.admin@example.com  (2 account(s))

CLI
  Version  v0.4.1
```

### Just print file paths

```bash
$ clustral config path
/Users/alice/.clustral/config.json
/Users/alice/.clustral/token
/Users/alice/.kube/config
```

### Script-friendly output

```bash
$ clustral config show --json | jq '.controlPlane.url'
"https://controlplane.clustral.example"

$ clustral config show --json | jq '.session.validForSeconds'
28391
```

### One-off profile override

```bash
$ CLUSTRAL_PROFILE=staging clustral clusters list
NAME             STATUS       AGENT    K8S
staging-eu-west  Connected    v0.3.2   v1.29
```

### Preview a factory reset

```bash
$ clustral config clean --dry-run

Would remove:
  ✗ ~/.clustral/config.json
  ✗ ~/.clustral/token
  ✗ ~/.clustral/active-profile
  ✗ ~/.clustral/profiles/  (2 profiles: prod, staging)
  ✗ 2 clustral-* kubeconfig context(s)

No changes made (dry run).
```

### Factory reset

```bash
$ clustral config clean --yes
✓ Cleaned
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Token file unreadable` | The `token` file was truncated or is not valid base64 JWT. | `clustral config clean` and `clustral login` again. |
| `Session expired` on every command | System clock skew or the IdP rotated its signing keys. | Sync time (NTP); `clustral login --force` re-fetches discovery. |
| Config still shows the old ControlPlane after switching profiles | You switched profiles but the command you ran still uses `--controlplane-url`. | Unset inline overrides; `clustral profiles current` to confirm. |
| `config.json` missing after `config clean` | Expected — `clean` removes it. | `clustral login <controlplane-url>` re-creates it. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | Generic error (unreadable config, missing files in non-interactive clean, write failure). |

## See also

- [`clustral login`](login.md) — what writes most of this state in the first place.
- [`clustral profiles`](README.md) — switch between environments.
- [`clustral accounts`](README.md) — switch between identities inside a profile.
- [`clustral status`](status.md) — same data plus live ControlPlane health.
