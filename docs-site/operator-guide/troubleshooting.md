---
description: Symptom-driven diagnostics for Clustral — toolkit, common failures, and how to escalate with the right evidence.
---

# Troubleshooting

Start with the user's correlation ID. Everything else is secondary.

## Overview

When something goes wrong, work outwards from the report:

1.  Get the **correlation ID** from the user's error message.
2.  Run `clustral status` or `clustral doctor` from a client — these surface most client-side misconfigurations in seconds.
3.  Check per-component health endpoints (see [Monitoring](monitoring.md)).
4.  Query the audit log by correlation ID.
5.  `docker compose logs <service> --since 15m` on whichever component looks suspect.

If you can't reproduce, still capture logs — Clustral's JSON log format makes after-the-fact grep cheap.

{% hint style="info" %}
Every response — success or failure — carries `X-Correlation-Id`. If a user reports an error without one, ask them to re-run with the same input and send the full response headers (or the stderr output from `kubectl --v=6`).
{% endhint %}

## Diagnostic toolkit

| Tool | What it tells you |
|---|---|
| `clustral status` | Session, visible clusters, active grants, platform health — client-side snapshot. |
| `clustral doctor` | Connectivity probes against the gateway, ControlPlane, and OIDC provider with specific failure messages. |
| `clustral audit --user <email> --from <iso>` | Recent audit events for one actor — narrow the window to the minute of the incident. |
| `/healthz/ready` per component | Is this component able to serve? |
| `/gateway/healthz/ready` | Is the gateway able to reach the OIDC provider? |
| `docker compose ps` / `kubectl get pods -n clustral-system` | Container / pod state. |
| `docker compose logs <svc> --since 15m` | Recent logs for one component. |
| `rabbitmqctl list_queues` | Audit event backlog. |
| `clustral clusters list` | Agent last-seen and version skew. |

Always pair a component-log grep with the correlation ID — full-text searches through 15 minutes of logs across four components find the right line instantly.

## Symptom → cause → fix

Organized by what the user or operator first sees. Most entries are short; the ones with subtle causes get a deep-dive.

### Quick reference

| Symptom | Most likely cause | First action |
|---|---|---|
| `kubectl` hangs forever | Agent disconnected | Check audit log for recent `CCL003W` (agent disconnected). |
| `error from server (Forbidden): no active role assignment` | User has no grant | User requests access via `clustral access request`. |
| Login succeeds in browser but CLI hangs exchanging the token | `HOST_IP` mismatch | Align `HOST_IP`, `NEXTAUTH_URL`, `OIDC_AUTHORITY`. |
| 401 on every REST call after login | OIDC audience mismatch | Compare token's `aud` claim to `OIDC_AUDIENCE`. |
| Gateway logs `JWT validation failed: invalid issuer` | Stale OIDC token from old hostname | Users re-login; or add old hostname to `Oidc:ValidIssuers` during cutover. |
| `kubectl` works for hours then stops | Kubeconfig JWT expired (8h default) | `clustral kube login <cluster>` again; raise `CREDENTIAL_DEFAULT_TTL` if policy allows. |
| Audit events missing or late | RabbitMQ backlog or AuditService not consuming | `rabbitmqctl list_queues` + AuditService logs. |
| Cluster listed but proxy returns `AGENT_NOT_CONNECTED` | Agent crashed or rescheduled | Check agent pod logs. |
| Every `kubectl` call returns 429 | Per-credential rate limit bit | Raise `PROXY_RATE_LIMITING_*` or investigate the caller. |
| MongoDB disk filling up | Audit database growth | Apply retention; check `clustral audit` by code for runaway events. |

### 1. `kubectl` hangs forever

**What happened.** `kubectl` is sending requests to the proxy, but the agent on the target cluster isn't reading them off the tunnel — either because it's disconnected or because the tunnel session is wedged.

**How to confirm.**
```bash
# Audit log for recent disconnects on this cluster
clustral audit --cluster <cluster-id> --code CCL003W --from $(date -u -v-1H +%FT%TZ)

# Cluster last-seen — if > 1 min, agent is effectively gone
clustral clusters list
```

**How to fix.** Restart the agent pod: `kubectl -n clustral-system rollout restart deployment/clustral-agent`. Then investigate why it dropped — check agent pod logs for panics, OOM kills, or TLS handshake failures.

### 2. `error from server (Forbidden): no active role assignment`

**What happened.** The user authenticated fine but has no role binding on the target cluster — neither a static assignment nor an active JIT grant.

**How to fix.** The user runs:
```bash
clustral access request --cluster <cluster> --role <role> --for 4h
```
An admin approves via `clustral access approve <id>` or the Web UI. The next `kubectl` call picks up the grant without re-running `kube login`.

### 3. `kubectl` returns `error: unknown`

**Edge case. Should not happen on current versions.** If you see this, the aggregated-discovery client in `kubectl` received a JSON body on the proxy path where it expects `v1.Status` or plain text. Clustral emits plain text on `/api/proxy/*` — message in the body, machine code in `X-Clustral-Error-Code`.

**How to confirm.** `curl -i https://<host>/api/proxy/<cluster>/api/v1/pods` and check the `Content-Type` header. Should be `text/plain`.

**How to fix.** Find the middle box rewriting content types (aggressive WAF, misconfigured reverse proxy) and stop it. See [ADR 001 — Error Response Shapes](https://github.com/Clustral/clustral/blob/main/docs/adr/001-error-response-shapes.md).

### 4. Login succeeds in browser, CLI hangs or fails to exchange the token

**What happened.** The URL the user hit in the browser and the issuer the gateway expects are different strings. Keycloak with `KC_HOSTNAME_STRICT=false` puts whatever hostname was used in the browser into the token's `iss` claim; if the gateway has `OIDC_AUTHORITY` pinned to a different hostname, validation fails silently and the CLI never gets a confirmed session.

**How to confirm.** Decode the stored token:
```bash
jq -R 'split(".") | .[1] | @base64d | fromjson' < ~/.clustral/token
```
Compare the `iss` claim with `OIDC_AUTHORITY` in `.env`.

**How to fix.** Make `HOST_IP`, `NEXTAUTH_URL`, and `OIDC_AUTHORITY` consistent. Either pick a canonical hostname everywhere, or list both in `Oidc:ValidIssuers` during a cutover.

### 5. 401 Unauthorized on every REST call after login

**What happened.** The token's `aud` claim doesn't match `OIDC_AUDIENCE` on the gateway. Keycloak audience mappers can be silently wrong — the `audience-controlplane` protocol mapper needs to be attached to every client that issues tokens for the ControlPlane (`clustral-cli`, `clustral-web`).

**How to confirm.** Paste the token into [jwt.io](https://jwt.io) (or `jq` as above) and compare the `aud` claim to `OIDC_AUDIENCE` in your gateway environment.

**How to fix.** Add the audience mapper on the Keycloak client, or list both audiences in `Oidc:ValidAudiences` on the gateway during a transition.

### 6. Gateway logs `JWT validation failed: invalid issuer`

**What happened.** The user still has a valid OIDC access token signed for an old hostname (e.g. the stack moved from `clustral-staging.corp.com` to `clustral.corp.com`). The token hasn't expired yet but its `iss` no longer matches.

**How to fix.** Choose one:

- Wait it out — OIDC access tokens are typically short-lived (≤ 1h). The user re-signs on expiry and the problem resolves.
- Have affected users run `clustral logout && clustral login`.
- Add the old hostname to `Oidc:ValidIssuers` in gateway appsettings during the cutover and remove it after tokens drain.

### 7. Credentials work for a while, then stop — re-login every N hours

**What happened.** The kubeconfig JWT expired. Default TTL is 8 hours, set by `Credential:DefaultKubeconfigCredentialTtl` and capped by `Credential:MaxKubeconfigCredentialTtl`.

**How to fix.** If the policy allows longer sessions, bump both settings (in `.env`: `CREDENTIAL_DEFAULT_TTL` and `CREDENTIAL_MAX_TTL`) and restart the ControlPlane. Otherwise, document the re-login flow (`clustral kube login <cluster>`) for users.

### 8. Audit events missing or arriving late

**What happened.** Events are queued in RabbitMQ but the AuditService isn't consuming — usually because the AuditService lost its MongoDB connection, crashed, or is running an older consumer than the publisher.

**How to confirm.**
```bash
# Queue depth — non-zero and growing is the symptom
docker exec rabbitmq rabbitmqctl list_queues name messages

# Consumer health
docker compose logs audit-service --since 15m | jq 'select(.@l=="Error")'

# MongoDB reachability
curl -fs http://localhost:5200/healthz/ready
```

**How to fix.** Restart the AuditService: `docker compose restart audit-service`. If the queue backs up for hours, MassTransit will redeliver on recovery — no events are lost unless the RabbitMQ volume itself is destroyed.

### 9. Cluster listed but proxy returns `AGENT_NOT_CONNECTED`

**What happened.** The cluster record exists in MongoDB from a previous registration, but no agent is currently connected to the ControlPlane for that cluster ID.

**How to confirm.**
```bash
# From the client
clustral clusters list
# → LastHeartbeat column shows > 1 min

# From inside the target cluster
kubectl -n clustral-system get pods -l app=clustral-agent
kubectl -n clustral-system logs -l app=clustral-agent --tail=200
```

**How to fix.** Get the agent back up — `kubectl rollout restart` usually does it. If the agent can't reach the ControlPlane's gRPC port (`:5443` direct, not through the gateway), check egress firewall rules from the target cluster.

### 10. Helm upgrade rolled out, then clusters went offline

**What happened.** The new agent can't complete the mTLS handshake with the ControlPlane — usually an expired client cert, a CA mismatch, or a missing bootstrap token.

**How to confirm.** ControlPlane logs show TLS handshake failures:
```bash
docker compose logs controlplane --since 15m | grep -i tls
```
Agent logs show cert-related errors.

**How to fix.** Issue a fresh bootstrap token from the ControlPlane (`POST /api/v1/clusters/{id}/bootstrap-tokens`) and redeploy the agent with it. See [mTLS Bootstrap](../agent-deployment/mtls-bootstrap.md).

### 11. Every `kubectl` returns 429

**What happened.** The per-credential token bucket fired. Defaults are 100 rps sustained / 200 burst / 50-deep queue (matching k8s client-go), per kubeconfig credential, enforced in the ControlPlane.

**How to fix.** Either raise the limits in `.env`:
```
PROXY_RATE_LIMITING_REQUESTS_PER_SECOND=200
PROXY_RATE_LIMITING_BURST_SIZE=400
PROXY_RATE_LIMITING_QUEUE_SIZE=100
```
and restart the ControlPlane — or investigate the workload. A tight `kubectl` loop in a CI script or a misbehaving controller is a common source. The audit log shows the triggering credential ID, which maps to a user.

### 12. MongoDB disk filling up

**What happened.** Almost always the audit database (`clustral-audit`). Audit events are small but high-volume, especially if proxy-level events are enabled.

**How to confirm.**
```bash
docker exec mongo mongosh --quiet --eval \
    'db.getSiblingDB("clustral-audit").stats().dataSize'

# Find the noisy codes — paginate the audit API and group by code
curl -H "Authorization: Bearer $(cat ~/.clustral/token)" \
    "https://<host>/audit-api/api/v1/audit?pageSize=200&page=1" | \
    jq -r '.items[].code' | sort | uniq -c | sort -rn | head
```

**How to fix.** Apply retention (see [Audit Log](../security-model/audit-log.md)). If retention is already configured and still growing, one event code is dominating — correlate with a runaway integration or a misbehaving user / workload.

## Escalating

If the above don't resolve it, collect this evidence before filing an issue:

- The correlation ID from the original error.
- The output of `clustral doctor` and `clustral status` from the affected client.
- The last 15 minutes of logs from each component:
  ```bash
  docker compose logs api-gateway controlplane audit-service web \
      --since 15m --no-color > clustral-logs.txt
  ```
- Recent audit events for the affected user, narrowed to the incident window:
  ```bash
  clustral audit --user <email> --from <incident-time-minus-5m> --json > audit.json
  ```
- Versions: `clustral version`, `curl -fs http://<host>/api/v1/version`, and `clustral clusters list` (for agent versions).

File at [github.com/Clustral/clustral/issues](https://github.com/Clustral/clustral/issues). Do not include raw JWTs in the report — redact the middle segment of any bearer token.

## See also

- [Monitoring](monitoring.md) — the signals these symptoms map back to.
- [Upgrade](upgrade.md) — most operational surprises correlate with a recent version bump.
- [Audit Log](../security-model/audit-log.md) — how to query event codes by user, cluster, and correlation ID.
- [CLI Reference](../cli-reference/README.md) — `clustral doctor`, `clustral status`, and `clustral audit` details.
