---
code: GRANT_NOT_APPROVED
http_status: 409
kind: Conflict
category: Access Request
emitted_by:
  - "ResultErrors.GrantNotApproved"
---

# GRANT_NOT_APPROVED

> **HTTP 409** | `Conflict` | Category: Access Request

<!-- AUTO-GEN-START -->
**Default message:** Only approved grants can be revoked. Current status: <placeholder>.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/grant-not-approved`](https://docs.clustral.kube.it.com/errors/grant-not-approved)
<!-- AUTO-GEN-END -->

## What this means

You are trying to revoke a grant that was never approved (e.g., it was denied or is still pending).

## Why it happens

- The access request was denied, not approved.
- The request is still pending review.

## How to fix

1. Check the request status: `clustral access list`.
2. Only approved grants can be revoked.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: GRANT_NOT_APPROVED
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/grant-not-approved",
  "title": "GRANT_NOT_APPROVED",
  "status": 409,
  "detail": "Only approved grants can be revoked. Current status: <placeholder>."
}
```

## See also

- [GRANT_ALREADY_REVOKED](grant-already-revoked.md) -- grant was already revoked
