---
code: REQUEST_NOT_PENDING
http_status: 409
kind: Conflict
category: Access Request
emitted_by:
  - "ResultErrors.RequestNotPending"
---

# REQUEST_NOT_PENDING

> **HTTP 409** | `Conflict` | Category: Access Request

**Default message:** Request is already <placeholder>.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/request-not-pending`](https://docs.clustral.kube.it.com/errors/request-not-pending)

## What this means

The access request cannot be approved or denied because it is no longer in the 'Pending' state. It was already processed.

## Why it happens

- Another administrator already approved or denied the request.
- The request expired before it was reviewed.

## How to fix

1. Check the request status: `clustral access list`.
2. If the user still needs access, ask them to submit a new request.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: REQUEST_NOT_PENDING
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/request-not-pending",
  "title": "REQUEST_NOT_PENDING",
  "status": 409,
  "detail": "Request is already <placeholder>."
}
```

## See also

- [REQUEST_EXPIRED](request-expired.md) -- request timed out
