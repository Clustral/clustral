---
code: UPSTREAM_UNAVAILABLE
http_status: 503
kind: Internal
category: Gateway
emitted_by:
  - "ApiGateway status-code handler"
---

# UPSTREAM_UNAVAILABLE

> **HTTP 503** | `Internal` | Category: Gateway

**Default message:** The upstream service is temporarily unavailable (e.g. during a rolling restart).

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/upstream-unavailable`](https://docs.clustral.kube.it.com/errors/upstream-unavailable)

## What this means

The upstream service responded with 503 Service Unavailable. It is reachable but temporarily cannot handle requests (e.g., during a rolling restart).

## Why it happens

- The service is in the middle of a restart.
- The service is overloaded and shedding load.

## How to fix

1. Retry after a few seconds.
2. If this persists, check the service health and logs.

## Example response

```
HTTP/1.1 503
Content-Type: text/plain
X-Clustral-Error-Code: UPSTREAM_UNAVAILABLE
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/upstream-unavailable>; rel="help"

The upstream service is temporarily unavailable (e.g. during a rolling restart).
```

## See also

- [UPSTREAM_UNREACHABLE](upstream-unreachable.md) -- service completely unreachable
