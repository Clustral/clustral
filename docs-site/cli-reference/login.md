---
description: Authenticate with the Clustral ControlPlane via OIDC Authorization Code + PKCE and cache the access token locally.
---

# `clustral login`

Run the OIDC Authorization Code + PKCE flow against the configured identity provider, then cache the resulting access token under `~/.clustral/`.

## Synopsis

```
clustral login [controlplane-url] [options]
```

The `controlplane-url` argument is optional. If omitted, the CLI uses `controlPlaneUrl` from `~/.clustral/config.json` (or, for a non-default profile, `~/.clustral/profiles/<name>/config.json`).

## Description

`clustral login` discovers OIDC settings from the ControlPlane (`/.well-known/clustral-configuration`), generates a PKCE verifier and challenge, opens your default browser, captures the authorization code on a loopback listener (`127.0.0.1:7777` by default), and exchanges it for an access token. The token is written under `~/.clustral/` — either to a per-account file (`accounts/<email>.token`) or to the legacy `token` file if the JWT has no email claim.

If a valid session already exists for the active profile, `clustral login` prints the current user profile and exits without re-authenticating. Use `--force` to re-run the browser flow anyway (for example, to switch accounts).

Side effects:

- Writes `~/.clustral/config.json` (ControlPlane URL, OIDC authority, client ID, scopes) on first use.
- Writes the access token to `~/.clustral/accounts/<email>.token` (or `~/.clustral/profiles/<profile>/accounts/<email>.token`).
- Sets the active account pointer for the current profile.

## Options

| Flag | Description | Default |
|---|---|---|
| `--port <n>` | Local TCP port for the OIDC callback listener. | `7777` |
| `--force` | Re-authenticate even if a valid session exists. | `false` |
| `--account <email>` | Store the token under an explicit account name instead of the JWT's `email` claim. | decoded from JWT |
| `--insecure` | Skip TLS verification on ControlPlane and OIDC calls (local dev only). | `false` |
| `--debug` | Verbose output: PKCE values, discovery responses, HTTP traces. | `false` |

## Examples

### First-time login against a known ControlPlane

```bash
$ clustral login controlplane.clustral.example
✓ Logged in  (profile: default)

  Profile URL   https://controlplane.clustral.example
  Logged in as  Alice Example
  Email         alice@example.com
  Kubernetes    enabled
  CLI version   v0.4.1
  Valid until   2026-04-14 18:42:03 +02:00  [valid for 7h58m]
  Roles         sre, viewer
  Clusters      prod-us-east, dev-sandbox
  Access        prod-us-east → viewer
                dev-sandbox  → sre
```

### Re-use cached URL on subsequent logins

```bash
$ clustral login
✓ Logged in  (profile: default)
  ...
```

### Switch to a different profile

```bash
$ clustral profiles use staging
$ clustral login controlplane.staging.clustral.example
```

The token and server config are stored under `~/.clustral/profiles/staging/`.

### Force re-authentication (switch account)

```bash
$ clustral login --force --account alice.admin@example.com
```

The OIDC provider is asked to show its login screen instead of reusing its SSO cookie. The resulting token is stored under `accounts/alice.admin@example.com.token`.

### Confirm identity after login

```bash
$ clustral whoami
alice@example.com — session valid for 7h 53m
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Browser opens but nothing happens after sign-in. | Loopback port `7777` is blocked or already bound. | Pass `--port 7778` (or any free port) and retry. |
| `401 Unauthorized` after sign-in succeeds. | Access token audience does not match the ControlPlane's expected audience. | Check `OIDC_AUDIENCE` in the server `.env` matches the client ID registered with your IdP. |
| Browser lands on "invalid redirect URI". | The hostname you signed in with is not registered as a valid redirect for the client. | Register `http://127.0.0.1:7777/` as a valid redirect URI on your OIDC client (Keycloak, Auth0, etc). |
| "Session expired" immediately after login. | Clock skew between your laptop and the IdP. | Sync system time (NTP) on both ends; skew > 30s rejects fresh tokens. |
| Login loops back to the error page. | The hostname you discovered does not match `HOST_IP` in server config. | Use the exact hostname declared in the server's OIDC redirect URI, or update `HOST_IP` in `infra/.env`. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Logged in (or existing session still valid). |
| 1 | Generic error (missing config, token write failed, OIDC exchange failed). |

## See also

- [`clustral kube login`](kube-login.md) — next step: mint a kubeconfig credential.
- [`clustral status`](status.md) — verify the session and reachable clusters.
- [`clustral config`](config.md) — inspect stored profile, account, and token state.
- [Authentication Flows](../architecture/authentication-flows.md) — end-to-end OIDC + kubeconfig JWT design.
