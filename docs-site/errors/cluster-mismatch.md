---
code: CLUSTER_MISMATCH
http_status: 403
kind: Forbidden
category: Cluster & Role
emitted_by:
  - "ResultErrors.ClusterMismatch"
---

# CLUSTER_MISMATCH

> **HTTP 403** | `Forbidden` | Category: Cluster & Role

**Default message:** This kubeconfig credential was issued for cluster 00000000-0000-0000-0000-000000000000 but the request is for cluster 00000000-0000-0000-0000-000000000000. Run 'clustral kube login 00000000-0000-0000-0000-000000000000' to switch credentials.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/cluster-mismatch`](https://docs.clustral.kube.it.com/errors/cluster-mismatch)

## What this means

The kubeconfig credential was issued for a specific cluster, but the proxy request targets a different cluster. Credentials are scoped to prevent cross-cluster access.

## Why it happens

- Your kubeconfig has entries for multiple clusters and you switched contexts without re-running `clustral kube login`.
- The credential was issued for cluster A but the kubectl request is routed to cluster B.

## How to fix

1. Run `clustral kube login <target-cluster>` to get a credential for the correct cluster.
2. Verify your current kubectl context: `kubectl config current-context`.

## Example response

```
HTTP/1.1 403
Content-Type: text/plain
X-Clustral-Error-Code: CLUSTER_MISMATCH
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/cluster-mismatch>; rel="help"

This kubeconfig credential was issued for cluster 00000000-0000-0000-0000-000000000000 but the request is for cluster 00000000-0000-0000-0000-000000000000. Run 'clustral kube login 00000000-0000-0000-0000-000000000000' to switch credentials.
```

## See also

- [NO_ROLE_ASSIGNMENT](no-role-assignment.md) -- no role for the target cluster
