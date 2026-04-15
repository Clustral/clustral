---
code: ROLE_NOT_FOUND
http_status: 404
kind: NotFound
category: Cluster & Role
emitted_by:
  - "ResultErrors.RoleNotFound"
---

# ROLE_NOT_FOUND

> **HTTP 404** | `NotFound` | Category: Cluster & Role

**Default message:** Role '<placeholder>' not found.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/role-not-found`](https://docs.clustral.kube.it.com/errors/role-not-found)

## What this means

The specified role ID does not match any role in the ControlPlane database.

## Why it happens

- The role was deleted.
- The role ID is incorrect.

## How to fix

1. List available roles: `clustral roles list`.
2. Use the correct role ID or name.

## Example response

```
HTTP/1.1 404
Content-Type: application/problem+json
X-Clustral-Error-Code: ROLE_NOT_FOUND
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/role-not-found",
  "title": "ROLE_NOT_FOUND",
  "status": 404,
  "detail": "Role '<placeholder>' not found."
}
```

## See also

- [DUPLICATE_ROLE_NAME](duplicate-role-name.md) -- creating a role with a taken name
