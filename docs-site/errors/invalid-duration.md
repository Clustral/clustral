---
code: INVALID_DURATION
http_status: 400
kind: BadRequest
category: Access Request
emitted_by:
  - "ResultErrors.InvalidDuration"
---

# INVALID_DURATION

> **HTTP 400** | `BadRequest` | Category: Access Request

**Default message:** Invalid ISO 8601 duration: '<placeholder>'.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/invalid-duration`](https://docs.clustral.kube.it.com/errors/invalid-duration)

## What this means

The requested duration is not a valid ISO 8601 duration string.

## Why it happens

- You passed an invalid format to `--duration` or `--ttl`.

## How to fix

1. Use ISO 8601 format: `PT8H` (8 hours), `PT30M` (30 minutes), `P1D` (1 day).
2. The CLI also accepts shorthand: `8H`, `30M`, `1D`.

## Example response

```
HTTP/1.1 400
Content-Type: application/problem+json
X-Clustral-Error-Code: INVALID_DURATION
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/invalid-duration",
  "title": "INVALID_DURATION",
  "status": 400,
  "detail": "Invalid ISO 8601 duration: '<placeholder>'."
}
```

## See also

- [VALIDATION_ERROR](validation-error.md) -- generic validation failure
