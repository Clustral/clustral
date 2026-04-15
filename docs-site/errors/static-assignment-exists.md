---
code: STATIC_ASSIGNMENT_EXISTS
http_status: 409
kind: Conflict
category: Access Request
emitted_by:
  - "ResultErrors.StaticAssignmentExists"
---

# STATIC_ASSIGNMENT_EXISTS

> **HTTP 409** | `Conflict` | Category: Access Request

**Default message:** You already have a static role assignment for this cluster.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/static-assignment-exists`](https://docs.clustral.kube.it.com/errors/static-assignment-exists)

## What this means

You already have a permanent (static) role assignment for this cluster. JIT access requests are only needed when you don't have a static assignment.

## Why it happens

- An administrator already assigned you a role for this cluster.

## How to fix

1. Run `clustral status` to see your current assignments.
2. If you need a different role, ask the administrator to update your static assignment.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: STATIC_ASSIGNMENT_EXISTS
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/static-assignment-exists",
  "title": "STATIC_ASSIGNMENT_EXISTS",
  "status": 409,
  "detail": "You already have a static role assignment for this cluster."
}
```

## See also

- [NO_ROLE_ASSIGNMENT](no-role-assignment.md) -- when you have neither static nor JIT access
