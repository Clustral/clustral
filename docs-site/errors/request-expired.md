---
code: REQUEST_EXPIRED
http_status: 409
kind: Conflict
category: Access Request
emitted_by:
  - "ResultErrors.RequestExpired"
---

# REQUEST_EXPIRED

> **HTTP 409** | `Conflict` | Category: Access Request

<!-- AUTO-GEN-START -->
**Default message:** Request has expired.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/request-expired`](https://docs.clustral.kube.it.com/errors/request-expired)
<!-- AUTO-GEN-END -->

## What this means

The access request expired before it was reviewed. Requests have a configurable expiry window.

## Why it happens

- No administrator reviewed the request within the allowed time.

## How to fix

1. Submit a new access request: `clustral access request --cluster <c> --role <r>`.
2. Notify an administrator to review it promptly.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: REQUEST_EXPIRED
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/request-expired",
  "title": "REQUEST_EXPIRED",
  "status": 409,
  "detail": "Request has expired."
}
```

## See also

- [REQUEST_NOT_PENDING](request-not-pending.md) -- request already processed
