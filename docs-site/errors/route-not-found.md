---
code: ROUTE_NOT_FOUND
http_status: 404
kind: NotFound
category: Gateway
emitted_by:
  - "ApiGateway status-code handler"
---

# ROUTE_NOT_FOUND

> **HTTP 404** | `NotFound` | Category: Gateway

**Default message:** No route matches the requested path. Check the URL and try again.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/route-not-found`](https://docs.clustral.kube.it.com/errors/route-not-found)

## What this means

No route in the API Gateway configuration matches the requested URL path.

## Why it happens

- The URL is misspelled.
- You are hitting the gateway directly instead of through the expected path.

## How to fix

1. Verify the URL path.
2. Check available endpoints in the API reference.

## Example response

```
HTTP/1.1 404
Content-Type: text/plain
X-Clustral-Error-Code: ROUTE_NOT_FOUND
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/route-not-found>; rel="help"

No route matches the requested path. Check the URL and try again.
```

## See also

- [GATEWAY_ERROR](gateway-error.md) -- generic gateway error
