---
code: CREDENTIAL_REVOKED
http_status: 401
kind: Unauthorized
category: User & Credential
emitted_by:
  - "ResultErrors.CredentialRevoked"
---

# CREDENTIAL_REVOKED

> **HTTP 401** | `Unauthorized` | Category: User & Credential

**Default message:** Your kubeconfig credential (00000000-0000-0000-0000-000000000000) has been revoked. Run 'clustral kube login <cluster>' to obtain a new one.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/credential-revoked`](https://docs.clustral.kube.it.com/errors/credential-revoked)

## What this means

The kubeconfig credential was explicitly revoked by you or an administrator before its natural expiration. Revoked credentials cannot be used again.

## Why it happens

- You ran `clustral kube logout <cluster>` which revokes the credential.
- An administrator revoked the credential via the Web UI or API.
- The credential was revoked as part of a security incident response.

## How to fix

1. Run `clustral kube login <cluster>` to issue a new credential.
2. If you didn't revoke the credential yourself, contact your administrator.

## Example response

```
HTTP/1.1 401
Content-Type: text/plain
X-Clustral-Error-Code: CREDENTIAL_REVOKED
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/credential-revoked>; rel="help"

Your kubeconfig credential (00000000-0000-0000-0000-000000000000) has been revoked. Run 'clustral kube login <cluster>' to obtain a new one.
```

## See also

- [CREDENTIAL_EXPIRED](credential-expired.md) -- credential expired naturally
- [AUTHENTICATION_REQUIRED](authentication-required.md) -- no credential provided
