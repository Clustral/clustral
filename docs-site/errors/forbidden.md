---
code: FORBIDDEN
http_status: 403
kind: Forbidden
category: Auth & Proxy
emitted_by:
  - "ResultErrors.CredentialOwnerMismatch"
  - "ResultErrors.AuthorizationFailed"
---

# FORBIDDEN

> **HTTP 403** | `Forbidden` | Category: Auth & Proxy

**Default message:** You can only revoke your own credentials.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/forbidden`](https://docs.clustral.kube.it.com/errors/forbidden)

## What this means

Your identity was verified but you lack permission for the requested action. On the proxy path this means your role doesn't cover this cluster; on the REST API it means you tried to modify a resource you don't own.

## Why it happens

- You are trying to revoke a credential that belongs to another user.
- Your role assignment does not include the target cluster.
- Your JIT access grant has been revoked or expired.

## How to fix

1. Check your current access: `clustral status`.
2. If you need access to a different cluster, request it: `clustral access request --cluster <name> --role <role>`.
3. Contact your administrator if you believe the denial is incorrect.

## Example response

```
HTTP/1.1 403
Content-Type: application/problem+json
X-Clustral-Error-Code: FORBIDDEN
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/forbidden",
  "title": "FORBIDDEN",
  "status": 403,
  "detail": "You can only revoke your own credentials."
}
```

## See also

- [NO_ROLE_ASSIGNMENT](no-role-assignment.md) -- no role for the specific cluster
- [AUTHENTICATION_REQUIRED](authentication-required.md) -- not authenticated at all
