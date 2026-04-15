---
code: CREDENTIAL_EXPIRED
http_status: 401
kind: Unauthorized
category: User & Credential
emitted_by:
  - "ResultErrors.CredentialExpired"
---

# CREDENTIAL_EXPIRED

> **HTTP 401** | `Unauthorized` | Category: User & Credential

**Default message:** Your kubeconfig credential (00000000-0000-0000-0000-000000000000) expired at 2025-01-01T00:00:00.0000000+00:00. Run 'clustral kube login <cluster>' to refresh it.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/credential-expired`](https://docs.clustral.kube.it.com/errors/credential-expired)

## What this means

The kubeconfig credential you are using has passed its expiration time. Kubeconfig credentials are short-lived (default 8 hours) and must be refreshed.

## Why it happens

- The credential's TTL elapsed since it was issued.
- You haven't re-run `clustral kube login` recently.

## How to fix

1. Run `clustral kube login <cluster>` to issue a fresh credential.
2. If you need longer-lived credentials, ask your administrator to increase the `MaxKubeconfigCredentialTtl` setting.

## Example response

```
HTTP/1.1 401
Content-Type: text/plain
X-Clustral-Error-Code: CREDENTIAL_EXPIRED
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/credential-expired>; rel="help"

Your kubeconfig credential (00000000-0000-0000-0000-000000000000) expired at 2026-04-14T08:43:09.0855190+00:00. Run 'clustral kube login <cluster>' to refresh it.
```

## See also

- [CREDENTIAL_REVOKED](credential-revoked.md) -- credential explicitly revoked before expiry
- [INVALID_TOKEN](invalid-token.md) -- generic token validation failure
