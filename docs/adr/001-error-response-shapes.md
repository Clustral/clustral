# ADR 001: Error Response Shapes

- **Date**: 2026-04-13
- **Status**: Accepted (superseded initial v1.Status decision with plain text on proxy path — see Revision below)

## Context

Before this decision, Clustral's HTTP error responses were inconsistent:

- REST controllers (`/api/v1/*`) returned RFC 7807 Problem Details via `ToActionResult()`.
- The kubectl proxy middleware (`/api/proxy/*`) wrote the `ResultError.Message` as **plain text with no machine-readable code**.
- The API Gateway's JwtBearer returned **empty 401/403** bodies.
- The API Gateway's rate limiter returned **empty 429** bodies.
- AuditService used ad-hoc `BadRequest("plain text")` for validation.
- YARP / no-route / CORS failures fell through to ASP.NET defaults.

Every consumer — kubectl, the Web UI, the CLI, future integrators — effectively saw a different error shape depending on which code path failed. The question: pick one shape and use it everywhere, or pick per path?

## Decision

**Path-aware split with kubectl-first design on the proxy path.** Clustral emits three HTTP error body shapes plus the native gRPC error model:

| Path | Shape | Content-Type | Error code location |
|---|---|---|---|
| `/api/proxy/*` (kubectl) | **Plain text** (self-speaking message) | `text/plain; charset=utf-8` | `X-Clustral-Error-Code` response header |
| All other HTTP endpoints | RFC 7807 Problem Details | `application/problem+json` | `extensions.code` in the body |
| gRPC (agent-facing) | gRPC `Status` (`RpcException`) | (gRPC trailers) | n/a |

Every response — success or failure — echoes `X-Correlation-Id`. The Clustral-specific error code is available as `X-Clustral-Error-Code` on the proxy path (where the body is plain text) and as `extensions.code` on RFC 7807 responses — so programmatic clients can switch on it uniformly regardless of which shape they received.

## Rationale

The enterprise principle is **"return the shape your consumer already speaks, and do so in a way that actually works in practice"**.

**Why plain text on the proxy path.** The original plan was to return Kubernetes `v1.Status` JSON on `/api/proxy/*` so kubectl would render `status.message` natively. In theory this aligns with every managed-Kubernetes provider (GKE/EKS/AKS/OpenShift). **In practice it doesn't work for kubectl v1.30+**:

- kubectl's aggregated-discovery client (used for `/api` and `/apis` before every command) builds its own `runtime.Scheme` that registers `apidiscoveryv2.APIGroupDiscoveryList` and the options types from `metav1.AddToGroupVersion` — **but not `metav1.Status`**.
- When it receives a 4xx/5xx with `application/json` + a valid `v1.Status` body, `runtime.DecodeInto(decoder, body, &Status{})` fails with "no kind registered for v1/Status".
- client-go then falls back to `newUnstructuredResponseError(body, isTextResponse, ...)` in `rest/request.go`, which hardcodes `message := "unknown"` when `isTextResponse` is false.
- Result: kubectl prints `Error from server (Forbidden): unknown` regardless of what's in our body.
- Real k8s clusters never hit this because they allow anonymous discovery on `/api` — so the limitation is untested upstream.

Plain text bypasses the scheme problem entirely: client-go's `isTextResponse(resp)` returns true for `Content-Type: text/plain`, and the same fallback uses `message = strings.TrimSpace(body)` — the user sees our actual message.

We keep structure through channels that cost the client nothing. On the proxy path, every field a client would read from an RFC 7807 Problem Details body is exposed as a response header — **reaching parity with `Result<T>`**:

- `X-Clustral-Error-Code` — machine-readable code (`CLUSTER_MISMATCH`, `AGENT_NOT_CONNECTED`, etc.)
- `X-Clustral-Error-Field` — validation-error field name
- `X-Clustral-Error-Meta-<key>` — one header per `ResultError.Metadata` entry (cluster IDs, credential IDs, timestamps in ISO 8601, etc.)
- `X-Correlation-Id` — already echoed on every response

The body message is **deliberately self-speaking**: each one names what went wrong, which component is involved, and how the user fixes it (e.g., "Run 'clustral kube login <cluster>' to obtain a fresh credential").

**Why RFC 7807 for the REST API.** Our REST consumers (Web UI, CLI, future integrators) are general HTTP clients. RFC 7807 is the IETF-standardized shape for HTTP errors, ASP.NET Core's default, and what every non-Kubernetes tool already understands. Returning plain text on `/api/v1/clusters` would lose the structured error info these clients rely on.

**Why gRPC stays on `RpcException`.** It's a different transport with its own well-established error convention. Changing it would break every Go client we ship and buy nothing.

## Alternatives Considered

- **v1.Status JSON on the proxy path** (original plan). Rejected because it breaks kubectl's aggregated discovery — kubectl prints `unknown`. No configuration we control on the server side makes client-go's discovery scheme register `metav1.Status`; a client-go fix would take multiple kubectl releases to propagate.
- **RFC 7807 everywhere.** Same problem as v1.Status — any `application/*+json` body hits the same kubectl fallback. Rejected.
- **Discovery bypass** (allow `/api`, `/apis` to pass through to k8s without impersonation). Would make kubectl discovery succeed but leaks API-group information to unauthorized users and is architecturally surprising ("403 on one path, 200 on another"). Rejected.
- **Content-negotiation based on `Accept`.** Could send v1.Status to clients that advertise the aggregated-discovery types and plain text to others. Adds complexity and hides the simple rule. Rejected.
- **JSON:API error objects.** Narrower industry adoption than RFC 7807, doesn't fix the kubectl discovery limitation. Rejected.
- **Custom Clustral error schema.** Every new schema is a documentation + client-library burden; gains us nothing over two industry-standard shapes plus plain text for the one case that really needs it. Rejected.

## Consequences

### Positive

- **kubectl actually shows our messages.** End users see one-line errors that tell them what to do: `error: alice@corp.com has no active role on cluster 'prod'. Either ask an administrator to grant you a static role, or request just-in-time access with 'clustral access request --cluster prod --role <role-name>'.`
- REST clients keep the RFC 7807 shape their HTTP libraries already understand.
- A single Clustral-specific error code (`X-Clustral-Error-Code` header / `extensions.code`) lets programmatic clients branch uniformly regardless of which shape they got.
- `X-Correlation-Id` echoed on every response provides a simple cross-service grep key for debugging.
- Shared SDK writers (`ProblemDetailsWriter`, `PlainTextErrorWriter`) mean `ControlPlane`, `AuditService`, and `ApiGateway` all emit byte-identical bodies for the same input.

### Negative

- Two body shapes to document (mitigated by this ADR, the "Error Response Shapes" section in the README, and a canonical error-code table).
- Contributors adding a new endpoint under `/api/proxy/*` need to remember to use `PlainTextErrorWriter`, not `ProblemDetailsWriter`. Inline XML-doc comments on each writer and a reminder in the root `CLAUDE.md` mitigate this.
- Plain text loses the rich JSON structure for the body — integrators who need programmatic access to details like `causes[]` must derive them from the error code in the header plus context. In practice this has been sufficient.

### Neutral

- gRPC stays on `RpcException(Status)`. No effort required; already consistent.

## Revision history

- **2026-04-13 (initial)**: Chose v1.Status on the proxy path expecting kubectl's native rendering to work. Live testing against kubectl v1.32 revealed the aggregated-discovery scheme limitation described above.
- **2026-04-13 (revised)**: Pivoted to plain text on the proxy path with `X-Clustral-Error-Code` header. Messages rewritten to be self-speaking and actionable. Removed `K8sStatus` / `K8sStatusWriter` (no remaining consumer).
