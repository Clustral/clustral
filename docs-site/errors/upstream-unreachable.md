---
code: UPSTREAM_UNREACHABLE
http_status: 502
kind: Internal
category: Gateway
emitted_by:
  - "ApiGateway status-code handler"
---

# UPSTREAM_UNREACHABLE

> **HTTP 502** | `Internal` | Category: Gateway

**Default message:** The upstream service (ControlPlane or AuditService) is not reachable. It may be starting up or has crashed.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/upstream-unreachable`](https://docs.clustral.kube.it.com/errors/upstream-unreachable)

## What this means

The API Gateway could not connect to the upstream service (ControlPlane or AuditService). The backend may be down, starting up, or unreachable.

## Why it happens

- The ControlPlane or AuditService container has crashed.
- The service is still starting up after a restart.
- Docker networking issue between the gateway and the backend.

## How to fix

1. Check the health of the backend service: `docker compose ps`.
2. Check the service logs: `docker compose logs controlplane`.
3. Wait for the service to finish starting if it was recently restarted.

## Example response

```
HTTP/1.1 502
Content-Type: text/plain
X-Clustral-Error-Code: UPSTREAM_UNREACHABLE
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/upstream-unreachable>; rel="help"

The upstream service (ControlPlane or AuditService) is not reachable. It may be starting up or has crashed.
```

## See also

- [UPSTREAM_UNAVAILABLE](upstream-unavailable.md) -- service temporarily unavailable
- [UPSTREAM_TIMEOUT](upstream-timeout.md) -- service reachable but slow
