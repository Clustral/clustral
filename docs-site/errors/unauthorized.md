---
code: UNAUTHORIZED
http_status: 401
kind: Unauthorized
category: User & Credential
emitted_by:
  - "ResultErrors.UserUnauthorized"
  - "ResultErrors.InvalidCredential"
---

# UNAUTHORIZED

> **HTTP 401** | `Unauthorized` | Category: User & Credential

**Default message:** Authentication required.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/unauthorized`](https://docs.clustral.kube.it.com/errors/unauthorized)

## What this means

Generic authorization failure -- the request was authenticated but the token is not valid for the requested operation.

## Why it happens

- The OIDC token is valid but doesn't have the required claims or scope.
- An internal JWT validation failed.

## How to fix

1. Run `clustral login` to refresh your OIDC token.
2. If the error persists, check that your OIDC provider is issuing the expected claims.

## Example response

```
HTTP/1.1 401
Content-Type: application/problem+json
X-Clustral-Error-Code: UNAUTHORIZED
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/unauthorized",
  "title": "UNAUTHORIZED",
  "status": 401,
  "detail": "Authentication required."
}
```

## See also

- [AUTHENTICATION_REQUIRED](authentication-required.md) -- not authenticated
- [INVALID_TOKEN](invalid-token.md) -- token validation failure
