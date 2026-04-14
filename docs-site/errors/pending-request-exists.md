---
code: PENDING_REQUEST_EXISTS
http_status: 409
kind: Conflict
category: Access Request
emitted_by:
  - "ResultErrors.PendingRequestExists"
---

# PENDING_REQUEST_EXISTS

> **HTTP 409** | `Conflict` | Category: Access Request

<!-- AUTO-GEN-START -->
**Default message:** You already have a pending request for this cluster.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/pending-request-exists`](https://docs.clustral.kube.it.com/errors/pending-request-exists)
<!-- AUTO-GEN-END -->

## What this means

You already have a pending (unapproved) JIT access request for this cluster. Only one pending request per cluster per user is allowed.

## Why it happens

- You submitted a request and it hasn't been approved or denied yet.

## How to fix

1. Wait for the existing request to be reviewed: `clustral access list`.
2. If the pending request is stale, ask an admin to deny it so you can resubmit.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: PENDING_REQUEST_EXISTS
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/pending-request-exists",
  "title": "PENDING_REQUEST_EXISTS",
  "status": 409,
  "detail": "You already have a pending request for this cluster."
}
```

## See also

- [GRANT_ALREADY_ACTIVE](grant-already-active.md) -- you already have an active grant
