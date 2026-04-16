---
description: List roles and the Kubernetes groups they impersonate on cluster access.
---

# `clustral roles`

List roles defined on the ControlPlane. Each role maps to a set of Kubernetes groups that are impersonated when a user connects through the Clustral proxy.

## Synopsis

```
clustral roles list [--insecure] [--json]
clustral roles ls   [--insecure] [--json]
```

`list` and `ls` are aliases.

## Description

A Clustral role is a named container for one or more Kubernetes groups. When a user's request reaches the kubectl proxy, the `ImpersonationResolver` looks up the role they hold (static assignment or active JIT grant) and forwards the impersonation headers (`Impersonate-User`, `Impersonate-Group`) to the Kubernetes API server. Role → group mapping therefore defines what the user can do inside the cluster — it is the RBAC handle for Clustral's access model.

Administrative mutations (create, update, delete) currently happen through the API or Web UI — the CLI only exposes `list` today.

## Subcommands

### `clustral roles list` (alias `ls`)

List all roles.

| Flag | Description | Default |
|---|---|---|
| `--insecure` | Skip TLS verification. | `false` |
| `--json` | Emit machine-readable JSON. | `false` |

## Examples

### Default output

```bash
$ clustral roles list
Role     Description                                K8s Groups
sre      On-call engineer, full cluster access      clustral:sre, system:masters
viewer   Read-only access across all namespaces     clustral:viewer
dev      Developer, namespaced edit access          clustral:dev, clustral:namespaced-edit
auditor  Read-only plus audit log access            clustral:auditor, clustral:logs-reader
```

### JSON

```bash
$ clustral roles list --json | jq '.roles[] | {name, groups: .kubernetesGroups}'
{
  "name": "sre",
  "groups": ["clustral:sre", "system:masters"]
}
{
  "name": "viewer",
  "groups": ["clustral:viewer"]
}
```

### Look up one role's groups

```bash
$ clustral roles list --json \
  | jq -r '.roles[] | select(.name=="sre") | .kubernetesGroups[]'
clustral:sre
system:masters
```

### Use with `access request`

```bash
$ clustral roles list
$ clustral access request --cluster prod-us-east --role sre --duration 2H \
    --reason "incident triage"
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `No roles found.` | The deployment has no roles configured. | Create roles via the API or Web UI, or re-run any seed scripts from `infra/`. |
| `clustral access request` returns `ROLE_NOT_FOUND` | The role name was mistyped or the role was deleted. | Confirm the exact name with `clustral roles list`. |
| Role exists but `kubectl` still gets `forbidden` | The role's Kubernetes groups have no matching `ClusterRoleBinding` in the target cluster. | Apply the RBAC manifest for the groups in `K8s Groups` column; see [Agent Deployment](../agent-deployment/README.md). |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Query succeeded (including empty result set). |
| 1 | Not logged in, or HTTP error from the ControlPlane. |

## See also

- [`clustral access`](access.md) — file a JIT request for one of these roles.
- [`clustral users`](users.md) — see who has each role.
- [Security Model](../security-model/README.md) — how role impersonation is enforced on the proxy path.
