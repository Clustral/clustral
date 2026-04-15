---
code: BAD_REQUEST
http_status: 400
kind: BadRequest
category: Exception Handler
emitted_by:
  - "GlobalExceptionHandlerMiddleware"
---

# BAD_REQUEST

> **HTTP 400** | `BadRequest` | Category: Exception Handler

**Default message:** The request is malformed or contains invalid arguments.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/bad-request`](https://docs.clustral.kube.it.com/errors/bad-request)

## What this means

The request could not be processed because it is malformed or contains invalid arguments.

## Why it happens

- A required field is missing.
- A field value is the wrong type (e.g., string where a GUID is expected).
- The request body is not valid JSON.

## How to fix

1. Check the error detail for which field is invalid.
2. Refer to the API reference for the expected request format.

## Example response

```
HTTP/1.1 400
Content-Type: text/plain
X-Clustral-Error-Code: BAD_REQUEST
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/bad-request>; rel="help"

The request is malformed or contains invalid arguments.
```

## See also

- [VALIDATION_ERROR](validation-error.md) -- FluentValidation-level validation error
- [UNPROCESSABLE](unprocessable.md) -- semantically invalid request
