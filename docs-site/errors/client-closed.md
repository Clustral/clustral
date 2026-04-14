---
code: CLIENT_CLOSED
http_status: 499
kind: Internal
category: Exception Handler
emitted_by:
  - "GlobalExceptionHandlerMiddleware"
---

# CLIENT_CLOSED

> **HTTP 499** | `Internal` | Category: Exception Handler

<!-- AUTO-GEN-START -->
**Default message:** The client closed the connection before the server could respond.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/client-closed`](https://docs.clustral.kube.it.com/errors/client-closed)
<!-- AUTO-GEN-END -->

## What this means

The client disconnected before the server finished processing the request. The server logs this for observability but the client won't see this error directly.

## Why it happens

- The user pressed Ctrl+C during a long-running kubectl command.
- A network interruption dropped the connection.
- The HTTP client had a shorter timeout than the server.

## How to fix

1. No action needed if you intentionally cancelled the request.
2. If unintentional, check your network connectivity and retry.

## Example response

```
HTTP/1.1 499
Content-Type: text/plain
X-Clustral-Error-Code: CLIENT_CLOSED
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/client-closed>; rel="help"

The client closed the connection before the server could respond.
```

## See also

- [UPSTREAM_TIMEOUT](upstream-timeout.md) -- server-side timeout
