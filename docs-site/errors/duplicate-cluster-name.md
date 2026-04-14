---
code: DUPLICATE_CLUSTER_NAME
http_status: 409
kind: Conflict
category: Cluster & Role
emitted_by:
  - "ResultErrors.DuplicateClusterName"
---

# DUPLICATE_CLUSTER_NAME

> **HTTP 409** | `Conflict` | Category: Cluster & Role

<!-- AUTO-GEN-START -->
**Default message:** Cluster named '<placeholder>' already exists.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/duplicate-cluster-name`](https://docs.clustral.kube.it.com/errors/duplicate-cluster-name)
<!-- AUTO-GEN-END -->

## What this means

A cluster with the same name already exists. Cluster names must be unique within a Clustral instance.

## Why it happens

- You are registering a cluster with a name that is already taken.
- A previous cluster with this name was not deleted before re-registering.

## How to fix

1. Choose a different name for the cluster.
2. If the existing cluster is stale, delete it first: `clustral clusters list` to find it, then delete via the Web UI or API.

## Example response

```
HTTP/1.1 409
Content-Type: application/problem+json
X-Clustral-Error-Code: DUPLICATE_CLUSTER_NAME
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/duplicate-cluster-name",
  "title": "DUPLICATE_CLUSTER_NAME",
  "status": 409,
  "detail": "Cluster named '<placeholder>' already exists."
}
```

## See also

- [CLUSTER_NOT_FOUND](cluster-not-found.md) -- looking up a cluster that doesn't exist
