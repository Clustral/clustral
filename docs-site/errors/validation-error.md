---
code: VALIDATION_ERROR
http_status: 422
kind: Validation
category: Validation & Generic
emitted_by:
  - "ResultErrors.RequiredField"
---

# VALIDATION_ERROR

> **HTTP 422** | `Validation` | Category: Validation & Generic

**Default message:** '<placeholder>' is required.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/validation-error`](https://docs.clustral.kube.it.com/errors/validation-error)

## What this means

One or more fields in the request failed FluentValidation rules. The error detail specifies which field and why.

## Why it happens

- A required field was empty or null.
- A field value didn't match the expected format (e.g., GUID, ISO 8601 duration).
- A string field exceeded the maximum length.

## How to fix

1. Read the error detail to identify the invalid field.
2. Fix the value and retry.
3. For CLI commands, check `clustral <command> --help` for expected formats.

## Example response

```
HTTP/1.1 422
Content-Type: application/problem+json
X-Clustral-Error-Code: VALIDATION_ERROR
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/validation-error",
  "title": "VALIDATION_ERROR",
  "status": 422,
  "detail": "'<placeholder>' is required."
}
```

## See also

- [BAD_REQUEST](bad-request.md) -- request-level validation
- [INVALID_FORMAT](invalid-format.md) -- specific format violation
