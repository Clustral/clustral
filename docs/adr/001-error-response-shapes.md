# ADR 001: Error Response Shapes

- **Date**: 2026-04-13
- **Status**: Accepted

## Context

Before this decision, Clustral's HTTP error responses were inconsistent:

- REST controllers (`/api/v1/*`) returned RFC 7807 Problem Details via `ToActionResult()`.
- The kubectl proxy middleware (`/api/proxy/*`) wrote the `ResultError.Message` as **plain text**, discarding all structure.
- The API Gateway's JwtBearer returned **empty 401/403** bodies.
- The API Gateway's rate limiter returned **empty 429** bodies.
- AuditService used ad-hoc `BadRequest("plain text")` for validation.
- YARP / no-route / CORS failures fell through to ASP.NET defaults.
- The Go agent already forwarded real k8s `v1.Status` bodies unchanged on the happy path.

Every consumer — kubectl, the Web UI, the CLI, future integrators — effectively saw a different error shape depending on which code path failed. The question: pick one shape and use it everywhere, or pick per path?

## Decision

**Path-aware split.** Clustral emits two HTTP error body shapes plus the native gRPC error model:

| Path | Shape | Content-Type |
|---|---|---|
| `/api/proxy/*` (kubectl) | Kubernetes `v1.Status` | `application/json` |
| All other HTTP endpoints | RFC 7807 Problem Details | `application/problem+json` |
| gRPC (agent-facing) | gRPC `Status` (`RpcException`) | (gRPC trailers) |

Every response — success or failure — echoes `X-Correlation-Id`. The Clustral-specific error code lives in `extensions.code` (RFC 7807) or `details.causes[0].reason` (`v1.Status`) so programmatic clients can switch on it uniformly.

## Rationale

The enterprise principle is **"return the shape your consumer already speaks"**.

**Why `v1.Status` on the proxy path.** `/api/proxy/*` is semantically a Kubernetes API surface — it forwards to one. kubectl, client-go, operators, and every k8s-ecosystem tool expect `v1.Status` and natively render `status.message` as `"error: <message>"`. Every managed-Kubernetes vendor (GKE, EKS, AKS, OpenShift, Rancher) returns `v1.Status` on their k8s API surfaces, even when their own control-plane APIs use different shapes. Returning RFC 7807 here would make kubectl print `{"type":"urn:...","title":"..."}` as a raw JSON blob instead of the familiar one-line error.

**Why RFC 7807 for the REST API.** Our REST consumers (Web UI, CLI, future integrators) are general HTTP clients. RFC 7807 is the IETF-standardized shape for HTTP errors, ASP.NET Core's default, and what every non-Kubernetes tool already understands. Returning `v1.Status` on `/api/v1/clusters` would force the Web UI to parse a Kubernetes-specific shape for something that has nothing to do with Kubernetes semantically.

**Why gRPC stays on `RpcException`.** It's a different transport with its own well-established error convention. Changing it would break every Go client we ship and buy nothing; the agent's gRPC errors are internal infrastructure, not a user-facing API surface.

## Alternatives Considered

- **RFC 7807 everywhere.** Genuinely universal and the only formal IETF standard. **Rejected** because kubectl loses its native error rendering — every kubectl user would see JSON blobs instead of single-line errors. Bad UX for the primary consumer of the proxy path.
- **`v1.Status` everywhere.** Uniform wire format. **Rejected** because non-Kubernetes consumers (Web UI, CLI, integrators) would parse a Kubernetes-specific shape for non-Kubernetes-y errors. Philosophically weird — returning `kind: "Status"` on `/api/v1/clusters` misrepresents what the endpoint is.
- **JSON:API error objects.** Narrower industry adoption than RFC 7807, no UX win for kubectl. **Rejected.**
- **Custom Clustral error schema.** Every new schema is a documentation + client-library burden; gains us nothing over the combination of two industry-standard shapes. **Rejected.**

## Consequences

### Positive

- kubectl renders errors as one-line messages the way k8s operators expect — no UX regression for the primary consumer of the proxy path.
- REST clients keep getting the RFC 7807 shape their HTTP libraries already understand.
- A single Clustral-specific error code (`extensions.code` / `details.causes[0].reason`) lets programmatic clients branch uniformly regardless of which shape they got.
- `X-Correlation-Id` echoed on every response provides a simple cross-service grep key for debugging.
- Shared SDK writers (`ProblemDetailsWriter`, `K8sStatusWriter`) mean `ControlPlane`, `AuditService`, and `ApiGateway` all emit byte-identical bodies for the same input.

### Negative

- Two shapes to document — mitigated by this ADR, the "Error Response Shapes" section in the README, and the canonical error-code table.
- Contributors adding new endpoints need to pick the right writer. Mitigated by inline XML-doc comments on each writer pointing at this ADR, and a sentence in the root `CLAUDE.md` telling them which writer to use and when.
- The path-aware branch in `GatewayErrorWriter` adds one conditional — small and well-documented.

### Neutral

- gRPC stays on `RpcException(Status)`. No effort required; already consistent.
