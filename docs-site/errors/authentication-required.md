---
code: AUTHENTICATION_REQUIRED
http_status: 401
kind: Unauthorized
category: Auth & Proxy
emitted_by:
  - "ResultErrors.AuthenticationRequired"
---

# AUTHENTICATION_REQUIRED

> **HTTP 401** | `Unauthorized` | Category: Auth & Proxy

**Default message:** Authentication required: this request is missing the 'Authorization: Bearer <token>' header. Run 'clustral kube login <cluster>' to obtain a fresh kubeconfig credential.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/authentication-required`](https://docs.clustral.kube.it.com/errors/authentication-required)

## What this means

The request reached the Clustral platform without a bearer token in the `Authorization` header. Every API and proxy request must include a valid token.

## Why it happens

- You ran `kubectl` without first running `clustral kube login <cluster>`.
- Your kubeconfig entry is missing or has an empty token field.
- You called the REST API without an `Authorization: Bearer <token>` header.

## How to fix

1. Run `clustral kube login <cluster>` to obtain a fresh kubeconfig credential.
2. For REST API calls, run `clustral login` first and include the JWT as a Bearer token.
3. Verify your kubeconfig with `clustral config show`.

## Example response

```
HTTP/1.1 401
Content-Type: text/plain
X-Clustral-Error-Code: AUTHENTICATION_REQUIRED
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/authentication-required>; rel="help"

Authentication required: this request is missing the 'Authorization: Bearer <token>' header. Run 'clustral kube login <cluster>' to obtain a fresh kubeconfig credential.
```

## See also

- [INVALID_TOKEN](invalid-token.md) -- token present but rejected
- [CREDENTIAL_EXPIRED](credential-expired.md) -- token was valid but has expired
