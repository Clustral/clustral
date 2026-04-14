---
code: UPSTREAM_TIMEOUT
http_status: 504
kind: Internal
category: Gateway
emitted_by:
  - "ApiGateway status-code handler"
---

# UPSTREAM_TIMEOUT

> **HTTP 504** | `Internal` | Category: Gateway

<!-- AUTO-GEN-START -->
**Default message:** The upstream service did not respond within the configured timeout.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/upstream-timeout`](https://docs.clustral.kube.it.com/errors/upstream-timeout)
<!-- AUTO-GEN-END -->

## What this means

The upstream service did not respond within the gateway's configured proxy timeout.

## Why it happens

- The backend is under heavy load.
- A long-running operation exceeded the timeout.

## How to fix

1. Retry the request.
2. If this is a proxy request, consider that the Kubernetes API on the target cluster may be slow.

## Example response

```
HTTP/1.1 504
Content-Type: text/plain
X-Clustral-Error-Code: UPSTREAM_TIMEOUT
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/upstream-timeout>; rel="help"

The upstream service did not respond within the configured timeout.
```

## See also

- [TUNNEL_TIMEOUT](tunnel-timeout.md) -- agent-specific timeout
