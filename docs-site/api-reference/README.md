---
description: REST API surface, authentication, pagination, error shapes, and a worked example per Clustral resource.
---

# REST API Reference

Clustral exposes a versioned REST API for cluster management, credential issuance, user and role administration, access requests, and audit log queries. This page is the overview; per-endpoint reference lands with the OpenAPI import.

## Overview

Two HTTP surfaces go through the same public entry (`https://<host>`):

- **`/api/v1/*`** — the ControlPlane API. CLI and Web UI traffic. Routed through the API Gateway.
- **`/audit-api/api/v1/*`** — the AuditService API. Queryable audit log. Routed through the API Gateway.

Every endpoint returns RFC 7807 Problem Details on error, **except** the kubectl proxy path (`/api/proxy/*`), which emits plain text so `kubectl` can render the body verbatim after `"error: "`. See [Error Reference](../errors/README.md) for the full error-code catalog and [ADR 001](https://github.com/Clustral/clustral/blob/main/docs/adr/001-error-response-shapes.md) for the rationale.

Swagger UI is available at `http://localhost:5100/swagger` on a dev ControlPlane.

## Base URLs

| Base URL | Routed to | Auth scheme |
|---|---|---|
| `https://<host>/api/v1/*` | ControlPlane (via gateway) | Bearer OIDC JWT or kubeconfig JWT (dispatched by `kind` claim) |
| `https://<host>/audit-api/api/v1/*` | AuditService (via gateway) | Bearer OIDC JWT |
| `https://<host>/api/proxy/<cluster-id>/*` | ControlPlane kubectl proxy | Bearer kubeconfig JWT |

Replace `<host>` with the FQDN you set as `HOST_IP` in `.env`. The gateway and the ControlPlane never speak to browsers or CLIs directly — nginx terminates TLS on `:443` and forwards to the gateway internally.

## Authentication

The API Gateway accepts two JWT schemes and dispatches based on the token's `kind` claim:

| `kind` claim | Issuer | Signing key | Used by |
|---|---|---|---|
| `oidc` (or missing) | Your IdP | IdP's JWKS | CLI, Web UI, scripts |
| `kubeconfig` | ControlPlane | ES256 (`infra/kubeconfig-jwt/`) | kubectl on `/api/proxy/*` |

Every request must include an `Authorization: Bearer <jwt>` header. The gateway enforces `aud == clustral-control-plane` on OIDC tokens and rejects anything whose issuer is not configured in `Oidc:ValidIssuers`.

See [Architecture — Authentication Flows](../architecture/authentication-flows.md) and [Security Model — mTLS & JWT Lifecycle](../security-model/mtls-jwt-lifecycle.md).

### Getting a token

Interactive (CLI / Web UI):

```bash
clustral login
clustral config token        # prints the OIDC access token
```

Machine-to-machine (CI/CD, scripts): use an OIDC client-credentials grant against your IdP, then pass the resulting token in the `Authorization` header. Your IdP client must map the `aud` claim to `clustral-control-plane` — the gateway rejects tokens without it.

```bash
TOKEN=$(curl -s -X POST "$OIDC_TOKEN_URL" \
  -d grant_type=client_credentials \
  -d client_id=$CI_CLIENT_ID \
  -d client_secret=$CI_CLIENT_SECRET \
  -d audience=clustral-control-plane \
  | jq -r .access_token)

curl -H "Authorization: Bearer $TOKEN" https://clustral.example.com/api/v1/clusters
```

## Correlation IDs and tracing

Every request may send `X-Correlation-Id` and every response echoes one. If you omit it, the gateway generates one. Use it to tie client-side failures to server-side logs and audit events.

The platform also participates in W3C Trace Context — send `traceparent` in, read `traceresponse` out. See [Monitoring](../operator-guide/monitoring.md) for the collector configuration.

```bash
curl -H "Authorization: Bearer $TOKEN" \
     -H "X-Correlation-Id: my-debug-session-42" \
     https://clustral.example.com/api/v1/clusters
# → response includes: X-Correlation-Id: my-debug-session-42
```

## Rate limits

| Path | Limiter | Default |
|---|---|---|
| `/api/v1/*` | YARP (gateway), policy `api` | Generous; tuned for interactive use |
| `/api/proxy/*` | Per-credential token bucket (ControlPlane `Proxy:RateLimiting`) | 100 rps sustained, 200 burst, 50 queue |

Exceeding a limit returns HTTP 429 with code `RATE_LIMITED`. Proxy-path defaults match Kubernetes client-go so a well-behaved `kubectl` never trips the limiter.

## Response shapes

### Success

Resource endpoints return the resource as JSON. List endpoints return an envelope:

```json
{
  "data": [ { "...": "..." } ],
  "nextPageToken": "eyJvZmZzZXQiOjUwfQ=="
}
```

### Error

RFC 7807 `application/problem+json`:

```json
{
  "type": "https://docs.clustral.kube.it.com/errors/cluster-not-found",
  "title": "Cluster not found",
  "status": 404,
  "detail": "No cluster with ID a3f7... exists.",
  "code": "CLUSTER_NOT_FOUND",
  "correlationId": "8f7e...",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

Clustral-specific extensions:

| Field | When set |
|---|---|
| `code` | Always. Machine-readable error code from the [Error Reference](../errors/README.md). |
| `field` | When the error is a validation failure against a specific request field. |
| `correlationId` | Always. Matches the `X-Correlation-Id` response header. |
| `traceId` | When the request was part of an OpenTelemetry trace. |

The kubectl proxy path returns plain-text bodies instead. The machine-readable code is in the `X-Clustral-Error-Code` response header.

## Pagination

List endpoints accept:

| Query param | Default | Max | Notes |
|---|---|---|---|
| `pageSize` | `50` | `200` | |
| `pageToken` | — | — | Opaque cursor from a previous `nextPageToken`. |

Total counts are not returned — aggregations over large tenants are expensive and almost never drive UI decisions. Follow cursors until `nextPageToken` is absent.

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "https://clustral.example.com/api/v1/clusters?pageSize=50"
# Response:
# { "data": [...], "nextPageToken": "eyJvZmZzZXQiOjUwfQ==" }

curl -H "Authorization: Bearer $TOKEN" \
  "https://clustral.example.com/api/v1/clusters?pageSize=50&pageToken=eyJvZmZzZXQiOjUwfQ=="
```

## Resources

### Clusters — `/api/v1/clusters`

Register, list, get, and deregister clusters. Register returns a single-use bootstrap token for the agent.

```bash
# Register
curl -X POST https://clustral.example.com/api/v1/clusters \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "name": "prod-us-east",
        "description": "Production US-East",
        "labels": { "env": "prod", "region": "us-east-1" }
      }'
```

```json
{
  "clusterId": "a3f7c1e0-2b4d-4a3f-8c9e-1b2d3f4a5c6d",
  "name": "prod-us-east",
  "bootstrapToken": "bst_ey...",
  "bootstrapTokenExpiresAt": null
}
```

List:

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "https://clustral.example.com/api/v1/clusters?status=Connected&pageSize=50"
```

Related errors: `CLUSTER_NOT_FOUND`, `CLUSTER_NAME_TAKEN`, `AGENT_NOT_CONNECTED`.

### Roles — `/api/v1/roles`

CRUD on Clustral roles. A role is a name plus the Kubernetes groups the agent impersonates when a user with that role connects.

```bash
curl -X POST https://clustral.example.com/api/v1/roles \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "name": "sre",
        "description": "On-call SRE",
        "kubernetesGroups": ["clustral:sre", "system:authenticated"]
      }'
```

```json
{
  "id": "7c9e0b2d-1a3f-4c5d-8e9f-a1b2c3d4e5f6",
  "name": "sre",
  "description": "On-call SRE",
  "kubernetesGroups": ["clustral:sre", "system:authenticated"]
}
```

Related errors: `ROLE_NOT_FOUND`, `ROLE_NAME_TAKEN`, `ROLE_IN_USE`.

### Users — `/api/v1/users`

List users, get the caller's canonical record, and manage per-cluster role assignments. Users are mirrored from your IdP on first login — there is no user-create endpoint.

```bash
curl -H "Authorization: Bearer $TOKEN" \
  https://clustral.example.com/api/v1/users/me
```

```json
{
  "id": "9d1e0f2a-...",
  "email": "alice@corp.com",
  "groups": ["clustral:sre"],
  "assignments": [
    { "id": "...", "clusterId": "a3f7...", "roleName": "sre" }
  ]
}
```

Assign a role on a cluster:

```bash
curl -X POST "https://clustral.example.com/api/v1/users/$USER_ID/assignments" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "roleId": "7c9e...", "clusterId": "a3f7..." }'
```

Related errors: `USER_NOT_FOUND`, `ASSIGNMENT_ALREADY_EXISTS`.

### Access Requests — `/api/v1/access-requests`

Full JIT (just-in-time) lifecycle: submit, list, get, approve, deny, revoke.

```bash
# Submit
curl -X POST https://clustral.example.com/api/v1/access-requests \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "roleId": "7c9e...",
        "clusterId": "a3f7...",
        "reason": "Investigating p0 pager",
        "requestedDuration": "PT4H"
      }'
```

```json
{
  "id": "b4d5e6f7-...",
  "status": "Pending",
  "roleName": "sre",
  "clusterName": "prod-us-east",
  "requestedDuration": "PT4H",
  "reason": "Investigating p0 pager",
  "createdAt": "2026-04-14T10:15:00Z"
}
```

Approve:

```bash
curl -X POST "https://clustral.example.com/api/v1/access-requests/$ID/approve" \
  -H "Authorization: Bearer $TOKEN"
# → 204 No Content
```

Deny (body required):

```bash
curl -X POST "https://clustral.example.com/api/v1/access-requests/$ID/deny" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "reason": "Use a scoped role instead." }'
```

List filters: `status=Pending|Approved|Denied|Revoked|Expired`, `mine=true`, `active=true`.

Related errors: `ACCESS_REQUEST_NOT_FOUND`, `ACCESS_REQUEST_ALREADY_DECIDED`, `ACCESS_REQUEST_EXPIRED`.

### Auth — `/api/v1/auth/*`

Issue and revoke kubeconfig credentials.

```bash
# Issue — returns an ES256-signed kubeconfig JWT (8h default TTL)
curl -X POST https://clustral.example.com/api/v1/auth/kubeconfig-credential \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "clusterId": "a3f7...", "requestedTtl": "PT8H" }'
```

```json
{
  "credentialId": "ce0f1a2b-...",
  "token": "ey...",
  "expiresAt": "2026-04-14T18:15:00Z",
  "serverUrl": "https://clustral.example.com",
  "cluster": { "id": "a3f7...", "name": "prod-us-east" }
}
```

Revoke by credential ID:

```bash
curl -X DELETE "https://clustral.example.com/api/v1/auth/credentials/$CRED_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "reason": "Laptop lost" }'
```

Revoke by token (used by `clustral kube logout`):

```bash
curl -X POST https://clustral.example.com/api/v1/auth/revoke-by-token \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "token": "ey..." }'
```

Related errors: `CREDENTIAL_NOT_FOUND`, `CREDENTIAL_REVOKED`, `TTL_TOO_LONG`.

### Audit — `/audit-api/api/v1/audit`

Query the audit log with filters. Every authentication, access-request state change, credential issuance, and proxy request lands here.

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "https://clustral.example.com/audit-api/api/v1/audit?category=AccessRequest&from=2026-04-01&pageSize=100"
```

```json
{
  "data": [
    {
      "id": "...",
      "eventCode": "CAR002I",
      "category": "AccessRequest",
      "severity": "Info",
      "actorEmail": "alice@corp.com",
      "clusterName": "prod-us-east",
      "occurredAt": "2026-04-14T10:16:02Z",
      "details": { "...": "..." }
    }
  ],
  "nextPageToken": null
}
```

Filters: `category`, `code`, `userEmail`, `clusterId`, `from`, `to`, `pageSize`, `pageToken`.

See [Security Model — Audit Log](../security-model/audit-log.md) for the event-code taxonomy.

## gRPC

Clustral exposes two gRPC services on the ControlPlane's dedicated agent port (`:5443`, mTLS + RS256 JWT):

| Service | Purpose |
|---|---|
| `ClusterService` | Agent registration, credential renewal, status updates |
| `TunnelService` | Bidirectional streaming — the kubectl tunnel |

End users do not call these directly; they are the contract between the agent and the control plane. Service definitions live in `packages/proto/` in the repository. See [Agent Deployment — mTLS Bootstrap](../agent-deployment/mtls-bootstrap.md) and [Architecture — Tunnel Lifecycle](../architecture/tunnel-lifecycle.md).

## Versioning

The API is versioned by URL prefix (`/api/v1`). Breaking changes ship as `/api/v2` alongside `/api/v1` for one major release cycle.

Deprecation warnings appear in response headers:

```
Deprecation: true
Sunset: Wed, 01 Oct 2026 00:00:00 GMT
Link: <https://docs.clustral.kube.it.com/api-reference>; rel="deprecation"
```

## See also

- [Architecture — Authentication Flows](../architecture/authentication-flows.md) — what the gateway validates on every token.
- [Error Reference](../errors/README.md) — full catalog of error codes, statuses, and fixes.
- [Security Model — Audit Log](../security-model/audit-log.md) — audit event categories and codes.
- [Operator Guide — Monitoring](../operator-guide/monitoring.md) — correlation IDs, traces, and metrics.
- [CLI Reference](../cli-reference/README.md) — how the CLI consumes these endpoints.
