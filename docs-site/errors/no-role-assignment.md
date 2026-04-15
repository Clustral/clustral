---
code: NO_ROLE_ASSIGNMENT
http_status: 403
kind: Forbidden
category: Auth & Proxy
emitted_by:
  - "ResultErrors.NoRoleAssignment"
---

# NO_ROLE_ASSIGNMENT

> **HTTP 403** | `Forbidden` | Category: Auth & Proxy

**Default message:** <placeholder> has no active role on cluster 00000000-0000-0000-0000-000000000000. Either ask an administrator to grant you a static role, or request just-in-time access with 'clustral access request --cluster 00000000-0000-0000-0000-000000000000 --role <role-name>' (run 'clustral clusters list' to map the cluster ID to its name).

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/no-role-assignment`](https://docs.clustral.kube.it.com/errors/no-role-assignment)

## What this means

Your identity was verified, but you have no role assignment (static or JIT) for the target cluster. The ControlPlane cannot determine which Kubernetes groups to impersonate, so the request is denied.

## Why it happens

- An administrator has not assigned you a role for this cluster.
- Your JIT access grant has expired or was revoked.
- You are targeting the wrong cluster (check the cluster ID in the URL).

## How to fix

1. Ask an administrator to assign you a role for the cluster via the Web UI or CLI.
2. Or request just-in-time access: `clustral access request --cluster <cluster> --role <role-name>`.
3. Check available roles with `clustral roles list` and your current access with `clustral status`.

## Example response

```
HTTP/1.1 403
Content-Type: text/plain
X-Clustral-Error-Code: NO_ROLE_ASSIGNMENT
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/no-role-assignment>; rel="help"

<placeholder> has no active role on cluster 00000000-0000-0000-0000-000000000000. Either ask an administrator to grant you a static role, or request just-in-time access with 'clustral access request --cluster 00000000-0000-0000-0000-000000000000 --role <role-name>' (run 'clustral clusters list' to map the cluster ID to its name).
```

## See also

- [FORBIDDEN](forbidden.md) -- authenticated but not authorized (generic)
- [CLUSTER_MISMATCH](cluster-mismatch.md) -- credential scoped to a different cluster
