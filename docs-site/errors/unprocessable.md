---
code: UNPROCESSABLE
http_status: 422
kind: BadRequest
category: Exception Handler
emitted_by:
  - "GlobalExceptionHandlerMiddleware"
---

# UNPROCESSABLE

> **HTTP 422** | `BadRequest` | Category: Exception Handler

**Default message:** The request is syntactically valid but semantically incorrect.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/unprocessable`](https://docs.clustral.kube.it.com/errors/unprocessable)

## What this means

The request is syntactically valid but semantically incorrect -- the server understood the format but cannot process the instruction.

## Why it happens

- A business rule prevents the operation (but no specific error code matched).
- An `InvalidOperationException` was thrown during processing.

## How to fix

1. Check the error detail message for specifics.
2. This is typically a catch-all -- look for a more specific error code if one exists.

## Example response

```
HTTP/1.1 422
Content-Type: text/plain
X-Clustral-Error-Code: UNPROCESSABLE
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/unprocessable>; rel="help"

The request is syntactically valid but semantically incorrect.
```

## See also

- [BAD_REQUEST](bad-request.md) -- syntactically invalid request
