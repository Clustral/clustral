# Clustral.ApiGateway — Claude Code Guide

YARP-based API Gateway that sits between nginx and downstream services.
Provides centralized authentication, rate limiting, CORS, correlation IDs,
and gRPC passthrough for agent mTLS tunnels.

---

## Architecture

```
nginx :443 (TLS) → API Gateway :8080 (HTTP) → ControlPlane :5100
                                             → AuditService :5200
Agent :5443 (gRPC) → API Gateway :5443 (passthrough) → ControlPlane :5443
```

---

## YARP Configuration

Routes and clusters are defined in `appsettings.json` under the
`ReverseProxy` section. YARP loads config at startup via
`LoadFromConfig()`.

### Routes

| Route | Path | Cluster | Auth | Rate Limit |
|---|---|---|---|---|
| `api-route` | `/api/{**catch-all}` | `controlplane` | Required | `api` policy |
| `healthz-route` | `/healthz/{**catch-all}` | `controlplane` | Anonymous | None |
| `audit-route` | `/audit-api/{**catch-all}` | `audit-service` | Required | `api` policy |
| `grpc-route` | `/{**catch-all}` (port 5443) | `controlplane-grpc` | Anonymous | None |

The `audit-route` uses `PathRemovePrefix: /audit-api` transform so
`/audit-api/api/v1/audit` → AuditService `/api/v1/audit`.

### Clusters

| Cluster | Address | Protocol |
|---|---|---|
| `controlplane` | `http://controlplane:5100` | HTTP/1.1 |
| `audit-service` | `http://audit-service:5200` | HTTP/1.1 |
| `controlplane-grpc` | `https://controlplane:5443` | HTTP/2 (gRPC passthrough) |

---

## Middleware Pipeline Order

1. **CORS** — configurable origins per environment
2. **Serilog request logging**
3. **Correlation ID** — generates or preserves `X-Correlation-Id`
4. **Authentication** — OIDC JWT validation (any provider)
5. **Authorization** — route-level policies (default = authenticated)
6. **Internal JWT** — issues ES256 token, adds `X-Internal-Token` header
7. **Rate Limiter** — per-user token bucket
8. **YARP reverse proxy** — routes to downstream clusters

---

## Internal JWT (ES256)

The gateway validates external OIDC JWTs and issues short-lived internal
JWTs for downstream services. This centralizes OIDC at the gateway — 
downstream services only validate a simple ES256 signature.

- **Algorithm:** ECDSA P-256 (ES256)
- **TTL:** 30 seconds (per-request, not cached)
- **Claims:** `sub`, `email`, `name`, `preferred_username`
- **Header:** `X-Internal-Token`
- **Key pair:** `infra/internal-jwt/private.pem` (gateway) + `public.pem` (downstream)

Uses `Clustral.Sdk.Auth.InternalJwtService`:
- `ForSigning(privateKeyPem)` — gateway
- `ForValidation(publicKeyPem)` — downstream

---

## gRPC Passthrough

Agent mTLS traffic on :5443 is forwarded to ControlPlane :5443 as a
transparent HTTP/2 proxy. YARP does NOT terminate TLS — the mTLS
handshake happens directly between the agent and ControlPlane.

The `controlplane-grpc` cluster uses:
```json
"HttpRequest": { "Version": "2", "VersionPolicy": "RequestVersionExact" }
```

---

## Configuration

| Setting | Location | Description |
|---|---|---|
| `Oidc:Authority` | env/appsettings | OIDC provider URL |
| `Oidc:Audience` | env/appsettings | Expected JWT audience |
| `InternalJwt:PrivateKeyPath` | env | ES256 private key for signing |
| `Cors:AllowedOrigins` | appsettings | Array of allowed CORS origins |

---

## Testing

Tests live in `src/Clustral.ApiGateway.Tests/`:
- Routing tests (WebApplicationFactory + downstream mocks)
- Auth + internal JWT tests
- Rate limiting tests
- Correlation ID tests
- CORS tests
- Request size limit tests
