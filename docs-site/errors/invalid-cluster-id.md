---
code: INVALID_CLUSTER_ID
http_status: 400
kind: BadRequest
category: Other
emitted_by:
  - "ResultErrors.InvalidClusterId"
---

# INVALID_CLUSTER_ID

> **HTTP 400** | `BadRequest` | Category: Other

<!-- AUTO-GEN-START -->
**Default message:** The cluster ID '<placeholder>' in the proxy URL is not a valid UUID. Your kubeconfig may be corrupt — re-run 'clustral kube login <cluster>' to regenerate it.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/invalid-cluster-id`](https://docs.clustral.kube.it.com/errors/invalid-cluster-id)
<!-- AUTO-GEN-END -->

## What this means

The cluster ID extracted from the proxy URL is not a valid UUID. The kubeconfig entry that kubectl is using has a malformed server URL.

## Why it happens

- The kubeconfig was manually edited and the cluster ID portion of the URL is corrupted.
- An older version of the CLI wrote a non-UUID cluster identifier.

## How to fix

1. Re-run `clustral kube login <cluster>` to regenerate the kubeconfig entry.
2. Or manually inspect `~/.kube/config` and fix the server URL.

## Example response

```
HTTP/1.1 400
Content-Type: text/plain
X-Clustral-Error-Code: INVALID_CLUSTER_ID
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/invalid-cluster-id>; rel="help"

The cluster ID '<placeholder>' in the proxy URL is not a valid UUID. Your kubeconfig may be corrupt — re-run 'clustral kube login <cluster>' to regenerate it.
```

## See also

- [CLUSTER_NOT_FOUND](cluster-not-found.md) -- valid UUID but no matching cluster
