---
description: Query audit events from the Clustral AuditService — filter by category, code, severity, user, cluster, or time range with pagination.
---

# `clustral audit`

Query audit events from the Clustral AuditService REST API with filters and pagination.

## Synopsis

```
clustral audit [--category <c>] [--code <c>] [--severity <s>]
               [--user <email>] [--cluster <id>]
               [--from <date>] [--to <date>]
               [--page <n>] [--page-size <n>]
               [--insecure] [--json]
```

Aliased `audit-log`.

## Description

The AuditService consumes integration events from the ControlPlane (via RabbitMQ) and persists them to MongoDB. `clustral audit` calls `GET /api/v1/audit` on the AuditService with the filters you pass as query parameters, then renders the result as a table (default) or JSON.

Every event has a stable `code` following the Teleport convention `[PREFIX][NUMBER][SEVERITY]` (for example `CAR002I` = Clustral Access Request, event 002, Info severity). Codes are the canonical way to look up a specific event type across ControlPlane versions.

The AuditService URL is discovered during `clustral login` and stored in `~/.clustral/config.json` as `auditServiceUrl`. Set it manually if you are running against a non-default topology.

## Options

| Flag | Description | Default |
|---|---|---|
| `--category <c>` | Filter by category: `access_requests`, `credentials`, `clusters`, `roles`, `auth`, `proxy`. | all |
| `--code <c>` | Filter by exact event code (e.g., `CAR002I`). | all |
| `--severity <s>` | Filter by severity: `Info`, `Warning`, `Error`. | all |
| `--user <email>` | Filter by actor email. | all |
| `--cluster <id>` | Filter by cluster ID (not name). | all |
| `--from <date>` | Start of time range, ISO 8601 date or datetime (e.g., `2026-04-01`). | unbounded |
| `--to <date>` | End of time range, inclusive. | unbounded |
| `--page <n>` | Page number, 1-based. | `1` |
| `--page-size <n>` | Events per page, 1–200. | `50` |
| `--insecure` | Skip TLS verification. | `false` |
| `--json` | Emit machine-readable JSON. | `false` |

## Examples

### Everything from today

```bash
$ clustral audit --from 2026-04-14
Code      Event                   User                Cluster        Time      Message
CAR002I   access_request.approved bob@example.com     prod-us-east   12m ago   Approved SRE grant for alice@example.com
CCL001I   credential.issued       alice@example.com   prod-us-east   11m ago   Kubeconfig credential issued
CAR003W   access_request.denied   bob@example.com     prod-eu-west   2h ago    Denied viewer grant for carol@example.com
```

### Just one user's access-request history

```bash
$ clustral audit --category access_requests --user alice@example.com
Code      Event                     User                Cluster        Time    Message
CAR001I   access_request.created    alice@example.com   prod-us-east   3h ago  Requested role 'sre' for 4h
CAR002I   access_request.approved   alice@example.com   prod-us-east   2h ago  Approved by bob@example.com
CAR005I   access_request.expired    alice@example.com   prod-us-east   1h ago  Grant window closed
```

### Warnings and errors only

```bash
$ clustral audit --severity Error --from 2026-04-13
Code      Event                User               Cluster        Time   Message
CPR010E   proxy.upstream_err   alice@example.com  prod-us-east   4h ago Tunnel closed: TUNNEL_TIMEOUT
```

### Machine-readable export

```bash
$ clustral audit --category credentials --from 2026-04-01 --page-size 200 --json \
  | jq '.events[] | {time, user, code, message}' \
  > credentials-april.jsonl
```

### Paging through a large result set

```bash
$ clustral audit --from 2026-04-01 --page 2 --page-size 100
... 100 rows ...

Page 2 of 7 (623 total events)
```

### One specific event code

```bash
$ clustral audit --code CAR002I --from 2026-04-14 --json \
  | jq '.events[] | {time, approver: .user, requester: .resourceName}'
{"time":"2026-04-14T12:04:11Z","approver":"bob@example.com","requester":"alice@example.com"}
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Query succeeded. Empty result set is still 0. |
| 1 | Generic error (AuditService URL not configured, HTTP error, unauthorized). |

## See also

- [Audit Log](../security-model/audit-log.md) — how events are produced, enriched, and stored.
- [`clustral access`](access.md) — commands that generate `CAR*` audit codes.
- [Error Reference](../errors/README.md) — error codes emitted on failed operations.
- [API Reference](../api-reference/README.md) — the raw `/api/v1/audit` endpoint.
