---
description: Sequential connectivity diagnostics that check configuration, DNS, TLS, ControlPlane health, OIDC discovery, JWT session, and kubeconfig.
---

# `clustral doctor`

Step through every layer of the CLI's connectivity stack — config, DNS, TLS, ControlPlane, OIDC, JWT, kubeconfig — and report pass/fail with timing.

## Synopsis

```
clustral doctor [--insecure] [--json]
```

## Description

`doctor` runs a fixed sequence of checks. Each check is timed and shown as one of:

- `✓ pass` — the check succeeded.
- `! warn` — degraded but not fatal (e.g., expired JWT, no kubeconfig contexts).
- `– skip` — the check did not apply (e.g., TLS check on an HTTP URL, or OIDC discovery when the authority is unconfigured).
- `✗ fail` — hard failure; the next check may be skipped because later layers depend on earlier ones.

The order is deliberate. A failure early on (no `ControlPlaneUrl` configured, DNS does not resolve, TLS handshake fails) short-circuits the remainder so the output points at the root cause instead of a cascade of downstream symptoms.

Checks performed:

1. **Configuration** — `~/.clustral/config.json` exists and has a `ControlPlaneUrl`.
2. **DNS resolution** — the ControlPlane host resolves to one or more IPs.
3. **TLS handshake** — only when the URL is `https://` and `--insecure` is off.
4. **ControlPlane health** — `GET /api/v1/version` returns 200.
5. **OIDC discovery** — `GET <oidcAuthority>/.well-known/openid-configuration` returns 200; skipped when authority is not configured.
6. **JWT session** — cached token exists and has an `exp` in the future.
7. **Kubeconfig** — at least one `clustral-*` context exists in `~/.kube/config`.

## Options

| Flag | Description | Default |
|---|---|---|
| `--insecure` | Skip TLS verification. Implied when `insecureTls` is set in `config.json`. | `false` |
| `--json` | Emit machine-readable JSON. | `false` |

## Examples

### Everything healthy

```bash
$ clustral doctor

  ✓ Configuration
    ControlPlane URL: https://controlplane.clustral.example
  ✓ DNS resolution (14ms)
    controlplane.clustral.example → 10.0.1.42
  ✓ TLS handshake (87ms)
    Valid certificate, status 200
  ✓ ControlPlane health (112ms)
    v0.4.1, 200 OK
  ✓ OIDC discovery (142ms)
    https://keycloak.example.com/realms/clustral (200 OK)
  ✓ JWT session
    Valid, expires 2026-04-14 18:42:03 [7h53m remaining]
  ✓ Kubeconfig
    2 clustral context(s), 2 with active credentials

All checks passed.
```

### Expired session

```bash
$ clustral doctor

  ✓ Configuration
  ✓ DNS resolution (9ms)
  ✓ TLS handshake (68ms)
  ✓ ControlPlane health (94ms)
  ✓ OIDC discovery (132ms)
  ! JWT session
    Expired at 2026-04-14 02:18:00. Run: clustral login
  ✓ Kubeconfig
    2 clustral context(s), 2 with active credentials

1 warning(s), 6 passed.
```

### Hard failure — no config

```bash
$ clustral doctor

  ✗ Configuration
    ControlPlane URL not configured. Run: clustral login <url>

1 failed, 0 warning(s), 0 passed.
```

### Machine-readable

```bash
$ clustral doctor --json | jq '.checks[] | select(.status == "fail")'
{
  "name": "ControlPlane health",
  "status": "fail",
  "detail": "Connection refused",
  "elapsedMs": 5012
}
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| DNS resolution fails | Split-horizon DNS; `HOST_IP` in server `.env` does not match your LAN. | Use `/etc/hosts` to pin the ControlPlane hostname, or fix the DNS zone. |
| TLS handshake fails with `certificate signed by unknown authority` | Your laptop does not trust the ControlPlane CA (self-signed dev cert). | Add the CA to your system trust store, or pass `--insecure` for local dev. |
| ControlPlane health shows `200 OK` but version mismatch | Agent/CLI/server were deployed from different branches. | Align versions; run `clustral update` and upgrade the server + agents. |
| OIDC discovery fails but ControlPlane passes | IdP is down, or the authority URL in `config.json` is stale. | Re-run `clustral login <url>` to refresh OIDC discovery. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | All checks passed or warned. |
| 1 | At least one check failed. |

## See also

- [`clustral status`](status.md) — lighter snapshot aimed at day-to-day use.
- [`clustral version`](version.md) — just the CLI + ControlPlane version.
- [`clustral config`](config.md) — inspect local state without probing the network.
- [Troubleshooting](../faq/README.md) — common failure patterns and fixes.
