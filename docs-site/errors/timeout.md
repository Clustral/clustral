---
code: TIMEOUT
http_status: 504
kind: Internal
category: Exception Handler
emitted_by:
  - "GlobalExceptionHandlerMiddleware"
---

# TIMEOUT

> **HTTP 504** | `Internal` | Category: Exception Handler

<!-- AUTO-GEN-START -->
**Default message:** The operation timed out before completing.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/timeout`](https://docs.clustral.kube.it.com/errors/timeout)
<!-- AUTO-GEN-END -->

## What this means

A server-side operation timed out (e.g., a `TaskCanceledException` or `TimeoutException` was thrown).

## Why it happens

- The operation took too long due to a slow database query or external dependency.
- The configured timeout is too short for the workload.

## How to fix

1. Retry the request.
2. If this is a recurring issue, check system resource utilization.

## Example response

```
HTTP/1.1 504
Content-Type: text/plain
X-Clustral-Error-Code: TIMEOUT
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/timeout>; rel="help"

The operation timed out before completing.
```

## See also

- [TUNNEL_TIMEOUT](tunnel-timeout.md) -- tunnel-specific timeout
- [UPSTREAM_TIMEOUT](upstream-timeout.md) -- gateway proxy timeout
