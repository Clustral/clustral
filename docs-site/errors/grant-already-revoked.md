---
code: GRANT_ALREADY_REVOKED
http_status: 409
kind: Conflict
category: Access Request
emitted_by:
  - "ResultErrors.GrantAlreadyRevoked"
---

# GRANT_ALREADY_REVOKED

> **HTTP 409** | `Conflict` | Category: Access Request

<!-- AUTO-GEN-START -->
**Default message:** Grant has already been revoked.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/grant-already-revoked`](https://docs.clustral.kube.it.com/errors/grant-already-revoked)
<!-- AUTO-GEN-END -->

## What this means

The JIT access grant has already been revoked. It cannot be revoked again.

## Why it happens

- The grant was revoked by you, the requester, or an administrator.

## How to fix

1. If you still need access, submit a new request: `clustral access request --cluster <c> --role <r>`.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: GRANT_ALREADY_REVOKED
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/grant-already-revoked",
  "title": "GRANT_ALREADY_REVOKED",
  "status": 409,
  "detail": "Grant has already been revoked."
}
```

## See also

- [GRANT_ALREADY_EXPIRED](grant-already-expired.md) -- grant expired naturally
