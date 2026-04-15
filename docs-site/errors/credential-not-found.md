---
code: CREDENTIAL_NOT_FOUND
http_status: 404
kind: NotFound
category: User & Credential
emitted_by:
  - "ResultErrors.CredentialNotFound"
---

# CREDENTIAL_NOT_FOUND

> **HTTP 404** | `NotFound` | Category: User & Credential

**Default message:** Credential not found.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/credential-not-found`](https://docs.clustral.kube.it.com/errors/credential-not-found)

## What this means

The specified credential ID does not exist in the database. This usually means the credential was already cleaned up or the ID is wrong.

## Why it happens

- The credential was already revoked and garbage-collected.
- The credential ID is a typo.

## How to fix

1. Issue a new credential: `clustral kube login <cluster>`.
2. If you're trying to revoke a credential, it may already be gone.

## Example response

```
HTTP/1.1 404
Content-Type: application/problem+json
X-Clustral-Error-Code: CREDENTIAL_NOT_FOUND
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/credential-not-found",
  "title": "CREDENTIAL_NOT_FOUND",
  "status": 404,
  "detail": "Credential not found."
}
```

## See also

- [CREDENTIAL_REVOKED](credential-revoked.md) -- credential explicitly revoked
- [CREDENTIAL_EXPIRED](credential-expired.md) -- credential naturally expired
