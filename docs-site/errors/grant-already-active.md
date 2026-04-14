---
code: GRANT_ALREADY_ACTIVE
http_status: 409
kind: Conflict
category: Access Request
emitted_by:
  - "ResultErrors.GrantAlreadyActive"
---

# GRANT_ALREADY_ACTIVE

> **HTTP 409** | `Conflict` | Category: Access Request

<!-- AUTO-GEN-START -->
**Default message:** You already have an active JIT grant for this cluster.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/grant-already-active`](https://docs.clustral.kube.it.com/errors/grant-already-active)
<!-- AUTO-GEN-END -->

## What this means

You already have an active JIT access grant for this cluster. You don't need to request access again until the current grant expires.

## Why it happens

- A previous access request was approved and the grant hasn't expired yet.

## How to fix

1. Use your existing grant: `clustral kube login <cluster>`.
2. Check when it expires: `clustral access list`.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: GRANT_ALREADY_ACTIVE
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/grant-already-active",
  "title": "GRANT_ALREADY_ACTIVE",
  "status": 409,
  "detail": "You already have an active JIT grant for this cluster."
}
```

## See also

- [PENDING_REQUEST_EXISTS](pending-request-exists.md) -- request still pending
