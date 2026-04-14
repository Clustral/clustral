---
code: RATE_LIMITED
http_status: 429
kind: Forbidden
category: Gateway
emitted_by:
  - "ApiGateway rate-limiter"
---

# RATE_LIMITED

> **HTTP 429** | `Forbidden` | Category: Gateway

<!-- AUTO-GEN-START -->
**Default message:** Too many requests. Slow down and retry after the period indicated in the Retry-After header.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/rate-limited`](https://docs.clustral.kube.it.com/errors/rate-limited)
<!-- AUTO-GEN-END -->

## What this means

The API Gateway rejected the request because the client exceeded the configured rate limit. A `Retry-After` header indicates when to retry.

## Why it happens

- You are sending requests faster than the configured QPS limit.
- A script or automation is calling the API in a tight loop.
- Multiple users share the same credential and collectively exceed the limit.

## How to fix

1. Wait for the duration indicated in the `Retry-After` response header.
2. Add backoff and retry logic to your automation.
3. If the default limits are too restrictive, ask your administrator to adjust the rate-limiting configuration.

## Example response

```
HTTP/1.1 429
Content-Type: text/plain
X-Clustral-Error-Code: RATE_LIMITED
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/rate-limited>; rel="help"

Too many requests. Slow down and retry after the period indicated in the Retry-After header.
```

## See also

- [GATEWAY_ERROR](gateway-error.md) -- generic gateway failure
