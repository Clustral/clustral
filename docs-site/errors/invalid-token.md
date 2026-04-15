---
code: INVALID_TOKEN
http_status: 401
kind: Unauthorized
category: Auth & Proxy
emitted_by:
  - "ResultErrors.TokenExpired"
  - "ResultErrors.TokenInvalidSignature"
  - "ResultErrors.TokenInvalidIssuer"
  - "ResultErrors.TokenInvalidAudience"
  - "ResultErrors.TokenNotYetValid"
  - "ResultErrors.TokenMissingExpiration"
  - "ResultErrors.TokenValidationFailed"
  - "ResultErrors.MalformedToken"
  - "ResultErrors.MissingSubjectClaim"
---

# INVALID_TOKEN

> **HTTP 401** | `Unauthorized` | Category: Auth & Proxy

**Default message:** Your bearer token has expired. Run 'clustral kube login <cluster>' to obtain a fresh kubeconfig credential.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/invalid-token`](https://docs.clustral.kube.it.com/errors/invalid-token)

## What this means

The bearer token in the request was present but failed validation. This covers multiple failure modes: expired tokens, bad signatures, wrong issuer, wrong audience, missing claims, and malformed JWTs.

## Why it happens

- The token has expired (check the `exp` claim).
- The token was signed by a different OIDC provider than the one configured on this Clustral instance.
- The token audience doesn't match the gateway's expected audience.
- The token is corrupt or truncated.
- System clock skew between the client and server.

## How to fix

1. Run `clustral kube login <cluster>` (for kubectl) or `clustral login` (for API calls) to get a fresh token.
2. If the error mentions 'issuer', verify that your CLI is pointed at the correct ControlPlane URL (`clustral config show`).
3. If the error mentions 'not yet valid', check your system clock.

## Example response

```
HTTP/1.1 401
Content-Type: text/plain
X-Clustral-Error-Code: INVALID_TOKEN
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/invalid-token>; rel="help"

Your bearer token has expired. Run 'clustral kube login <cluster>' to obtain a fresh kubeconfig credential.
```

## See also

- [AUTHENTICATION_REQUIRED](authentication-required.md) -- no token at all
- [CREDENTIAL_EXPIRED](credential-expired.md) -- kubeconfig credential specifically expired
- [CREDENTIAL_REVOKED](credential-revoked.md) -- kubeconfig credential revoked by admin
