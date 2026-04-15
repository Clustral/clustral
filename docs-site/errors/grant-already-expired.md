---
code: GRANT_ALREADY_EXPIRED
http_status: 409
kind: Conflict
category: Access Request
emitted_by:
  - "ResultErrors.GrantAlreadyExpired"
---

# GRANT_ALREADY_EXPIRED

> **HTTP 409** | `Conflict` | Category: Access Request

**Default message:** Grant has already expired.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/grant-already-expired`](https://docs.clustral.kube.it.com/errors/grant-already-expired)

## What this means

The JIT access grant has already expired. It cannot be revoked because there is nothing to revoke.

## Why it happens

- The grant's time window elapsed.

## How to fix

1. Submit a new access request if you still need access.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: GRANT_ALREADY_EXPIRED
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/grant-already-expired",
  "title": "GRANT_ALREADY_EXPIRED",
  "status": 409,
  "detail": "Grant has already expired."
}
```

## See also

- [GRANT_ALREADY_REVOKED](grant-already-revoked.md) -- grant was explicitly revoked
