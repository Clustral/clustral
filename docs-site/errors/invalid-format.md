---
code: INVALID_FORMAT
http_status: 400
kind: BadRequest
category: Validation & Generic
emitted_by:
  - "ResultErrors.InvalidFormat"
---

# INVALID_FORMAT

> **HTTP 400** | `BadRequest` | Category: Validation & Generic

**Default message:** <placeholder>

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/invalid-format`](https://docs.clustral.kube.it.com/errors/invalid-format)

## What this means

A specific field value doesn't match the expected format. More specific than VALIDATION_ERROR -- includes the field name and format requirement.

## Why it happens

- A GUID field contains a non-GUID string.
- An ISO 8601 duration field has an invalid format.
- A URL field is not a valid URI.

## How to fix

1. Check the error detail for the field name and expected format.
2. Fix the value and retry.

## Example response

```
HTTP/1.1 400
Content-Type: application/problem+json
X-Clustral-Error-Code: INVALID_FORMAT
X-Correlation-Id: <uuid>

{
  "type": "https://docs.clustral.kube.it.com/errors/invalid-format",
  "title": "INVALID_FORMAT",
  "status": 400,
  "detail": "<placeholder>"
}
```

## See also

- [VALIDATION_ERROR](validation-error.md) -- generic validation failure
