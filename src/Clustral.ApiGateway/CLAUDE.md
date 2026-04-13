# Clustral.ApiGateway — Claude Code Guide

YARP-based API Gateway that sits between nginx and downstream services.
Provides centralized authentication, rate limiting, CORS, and correlation IDs.
Handles all OIDC authentication centrally -- downstream services no longer
validate OIDC tokens. Issues ES256 internal JWTs (30s TTL) forwarded via
`X-Internal-Token` header. Also validates kubeconfig JWTs (ES256, issued by
ControlPlane) alongside OIDC JWTs.

---

## Architecture

```
nginx :443 (TLS) → API Gateway :8080 (HTTP) → ControlPlane :5100
                                             → AuditService :5200
Agent → ControlPlane :5443 (gRPC mTLS, direct — NOT through gateway)
```

Endpoints:
- `/gateway/healthz` — liveness check
- `/gateway/healthz/ready` — readiness check (includes OIDC provider connectivity)
- Kestrel on `:8080` HTTP only (no TLS — nginx handles that)

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

The `audit-route` uses `PathRemovePrefix: /audit-api` transform so
`/audit-api/api/v1/audit` → AuditService `/api/v1/audit`.

### Clusters

| Cluster | Address | Protocol |
|---|---|---|
| `controlplane` | `http://controlplane:5100` | HTTP/1.1 |
| `audit-service` | `http://audit-service:5200` | HTTP/1.1 |

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

## Kubeconfig JWT Validation

The gateway also validates **kubeconfig JWTs** (ES256, issued by the
ControlPlane via `KubeconfigJwtService`). The kubeconfig public key is
added as an additional `IssuerSigningKey` in the JwtBearer config.

The middleware tries OIDC JWKS keys first, then the kubeconfig ES256 key.
Both produce a valid `ClaimsPrincipal` → internal JWT is issued →
forwarded to downstream. kubectl proxy requests now flow through the
same auth pipeline as browser/CLI requests.

- **Key pair:** `infra/kubeconfig-jwt/private.pem` (ControlPlane) + `public.pem` (gateway)
- **Config:** `KubeconfigJwt:PublicKeyPath` env var

---

## gRPC Agent Traffic

Agents connect directly to ControlPlane :5443 for mTLS gRPC tunnels.
This traffic does NOT go through the gateway — mTLS is its own security
boundary. The gateway has no gRPC routes or passthrough configuration.

---

## Error Responses (path-aware)

The gateway returns two error body shapes depending on request path, via
`Api/GatewayErrorWriter.cs`:

- `/api/proxy/*` → Kubernetes `v1.Status` JSON (kubectl's native shape).
- Everything else → RFC 7807 `application/problem+json`.

Wired in four places:

| Integration point | File | What it handles |
|---|---|---|
| JwtBearer `OnChallenge` + `OnForbidden` (both schemes) | `Api/GatewayJwtEvents.cs` | Auth failures — classifies the exception and writes `AUTHENTICATION_REQUIRED` or `INVALID_TOKEN` with a specific reason (expired, bad signature, bad issuer, bad audience). |
| Rate limiter `OnRejected` | `Program.cs` (inline) | 429 responses — body includes `RATE_LIMITED` code. |
| Terminal status-code handler (`UseStatusCodePages`) | `Program.cs` (inline) | YARP 502 destination-unreachable, 404 no-route, CORS rejections — any response that would have had an empty body. |
| CORS / default fall-through | Handled by the terminal handler above. |

Every response echoes `X-Correlation-Id`. See `docs/adr/001-error-response-shapes.md`
for the rationale and the root `README.md` for the canonical error-code
table.

---

## Authentication — two strict schemes

The gateway runs **two distinct JwtBearer schemes** behind a policy scheme
(`JWT-Router`) that dispatches on the token's `kind` claim:

| Scheme | Validates | Issuer | Audience | Key |
|---|---|---|---|---|
| `OidcJwt` | external OIDC tokens (browser, CLI, kubectl login) | `Oidc:Authority` (+ `Oidc:ValidIssuers`) | `Oidc:Audience` (+ `Oidc:ValidAudiences`) | OIDC JWKS |
| `KubeconfigJwt` | kubeconfig credentials (ControlPlane-signed, used by `kubectl`) | `clustral-controlplane` | `clustral-kubeconfig` | ES256 public key |

Both schemes enforce issuer, audience, lifetime, and signing-key validation.
A compromised OIDC signing key cannot forge a kubeconfig token (different
expected issuer/audience + separate trust anchor) and vice versa.

Tokens without a recognized `kind` claim default to the OIDC scheme.

## Configuration

| Setting | Location | Description |
|---|---|---|
| `Oidc:Authority` | env/appsettings | OIDC provider issuer URL (becomes first `ValidIssuer`) |
| `Oidc:MetadataAddress` | env/appsettings | Optional JWKS metadata URL override (for Docker-internal DNS) |
| `Oidc:Audience` | env/appsettings | Primary expected JWT audience (becomes first `ValidAudience`) |
| `Oidc:ValidIssuers` | appsettings | Additional accepted issuer values (e.g., dev LAN IP vs localhost) |
| `Oidc:ValidAudiences` | appsettings | Additional accepted audience values (e.g., multi-client setups) |
| `Oidc:ClientId` | env/appsettings | OIDC client ID (informational) |
| `Oidc:NameClaimType` | env/appsettings | Claim mapped to `User.Identity.Name` (default: `preferred_username`) |
| `Oidc:RequireHttpsMetadata` | env/appsettings | Require HTTPS for OIDC metadata (default: true) |
| `InternalJwt:PrivateKeyPath` | env | ES256 private key for signing internal JWTs |
| `KubeconfigJwt:PublicKeyPath` | env | ES256 public key for validating kubeconfig JWTs |
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
