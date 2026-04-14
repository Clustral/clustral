---
code: CLUSTER_NOT_FOUND
http_status: 404
kind: NotFound
category: Cluster & Role
emitted_by:
  - "ResultErrors.ClusterNotFound"
---

# CLUSTER_NOT_FOUND

> **HTTP 404** | `NotFound` | Category: Cluster & Role

<!-- AUTO-GEN-START -->
**Default message:** Cluster '<placeholder>' not found.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/cluster-not-found`](https://docs.clustral.kube.it.com/errors/cluster-not-found)
<!-- AUTO-GEN-END -->

## What this means

The specified cluster ID does not match any registered cluster in the ControlPlane database.

## Why it happens

- The cluster was deleted.
- The cluster ID is a typo or was copied incorrectly.
- You are connected to a different ControlPlane instance than the one the cluster is registered on.

## How to fix

1. List registered clusters: `clustral clusters list`.
2. Verify the cluster ID or use the cluster name instead: `clustral kube login <cluster-name>`.
3. Check that your CLI is pointed at the correct ControlPlane: `clustral config show`.

## Example response

```
HTTP/1.1 404
Content-Type: application/problem+json
X-Clustral-Error-Code: CLUSTER_NOT_FOUND
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/cluster-not-found",
  "title": "CLUSTER_NOT_FOUND",
  "status": 404,
  "detail": "Cluster '<placeholder>' not found."
}
```

## See also

- [INVALID_CLUSTER_ID](invalid-cluster-id.md) -- malformed UUID in the proxy URL
