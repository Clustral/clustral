---
code: DUPLICATE_ROLE_NAME
http_status: 409
kind: Conflict
category: Cluster & Role
emitted_by:
  - "ResultErrors.DuplicateRoleName"
---

# DUPLICATE_ROLE_NAME

> **HTTP 409** | `Conflict` | Category: Cluster & Role

<!-- AUTO-GEN-START -->
**Default message:** Role named '<placeholder>' already exists.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/duplicate-role-name`](https://docs.clustral.kube.it.com/errors/duplicate-role-name)
<!-- AUTO-GEN-END -->

## What this means

A role with the same name already exists. Role names must be unique within a Clustral instance.

## Why it happens

- You are creating a role with a name that is already taken.

## How to fix

1. Choose a different name for the role.
2. If you want to modify an existing role, use the update endpoint instead.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: DUPLICATE_ROLE_NAME
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/duplicate-role-name",
  "title": "DUPLICATE_ROLE_NAME",
  "status": 409,
  "detail": "Role named '<placeholder>' already exists."
}
```

## See also

- [ROLE_NOT_FOUND](role-not-found.md) -- looking up a role that doesn't exist
