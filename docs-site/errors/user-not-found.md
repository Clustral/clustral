---
code: USER_NOT_FOUND
http_status: 404
kind: NotFound
category: User & Credential
emitted_by:
  - "ResultErrors.UserNotFound"
---

# USER_NOT_FOUND

> **HTTP 404** | `NotFound` | Category: User & Credential

<!-- AUTO-GEN-START -->
**Default message:** User not found.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/user-not-found`](https://docs.clustral.kube.it.com/errors/user-not-found)
<!-- AUTO-GEN-END -->

## What this means

No user record exists for the given ID. Users are automatically created on first OIDC login, so this typically means the user has never signed in.

## Why it happens

- The user has never logged in to this Clustral instance.
- The user ID is incorrect.
- The user record was manually deleted from the database.

## How to fix

1. Ask the user to run `clustral login <url>` to create their profile.
2. Verify the user ID via the Web UI or `clustral users list`.

## Example response

```
HTTP/1.1 404
Content-Type: application/problem+json
X-Clustral-Error-Code: USER_NOT_FOUND
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/user-not-found",
  "title": "USER_NOT_FOUND",
  "status": 404,
  "detail": "User not found."
}
```

## See also

- [AUTHENTICATION_REQUIRED](authentication-required.md) -- user not authenticated
