---
code: GATEWAY_ERROR
http_status: 500
kind: Internal
category: Gateway
emitted_by:
  - "ApiGateway status-code handler"
---

# GATEWAY_ERROR

> **HTTP 500** | `Internal` | Category: Gateway

<!-- AUTO-GEN-START -->
**Default message:** An unexpected error occurred in the API Gateway.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/gateway-error`](https://docs.clustral.kube.it.com/errors/gateway-error)
<!-- AUTO-GEN-END -->

## What this means

An unexpected error occurred inside the API Gateway (YARP reverse proxy). This is a catch-all for errors that don't fit a more specific category.

## Why it happens

- A bug in the gateway configuration.
- An unexpected exception during request processing.

## How to fix

1. Retry the request.
2. If the error persists, check the gateway container logs.
3. Report the issue with the `X-Correlation-Id` from the response.

## Example response

```
HTTP/1.1 500
Content-Type: text/plain
X-Clustral-Error-Code: GATEWAY_ERROR
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/gateway-error>; rel="help"

An unexpected error occurred in the API Gateway.
```

## See also

- [UPSTREAM_UNREACHABLE](upstream-unreachable.md) -- backend service down
- [RATE_LIMITED](rate-limited.md) -- rate limit exceeded
