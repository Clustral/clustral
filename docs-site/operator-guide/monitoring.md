---
description: What to scrape, what to alert on, and how to correlate across Clustral components with health endpoints, Prometheus metrics, and structured logs.
---

# Monitoring

Three instrumentation surfaces cover Clustral: health endpoints, Prometheus metrics, and structured JSON logs. This page tells you what to scrape, what to alert on, and how to correlate events across components.

## Overview

Every Clustral component emits:

- **Health endpoints** for Kubernetes / Docker liveness and readiness probes.
- **Structured JSON logs** on stdout via Serilog, with correlation IDs threaded through every request.
- **W3C Trace Context** headers (`traceparent`, `traceresponse`) for distributed tracing.
- **Prometheus `/metrics`** on the AuditService. First-party metrics for the ControlPlane and API Gateway are on the roadmap; until then, derive them from structured logs.

The correlation ID (`X-Correlation-Id`) is the primary stitching key. Every HTTP response and every log line carries it. When a user reports a failure, copy the correlation ID from their error, grep every component's logs, and you have the full chain.

{% hint style="info" %}
The audit log is the second stitching key. Every state-changing action in Clustral emits an integration event that the AuditService persists in MongoDB with the triggering user, cluster, and correlation ID. Query it via `clustral audit` or `GET /audit-api/api/v1/audit`.
{% endhint %}

## Health endpoints

| Endpoint | Service | Purpose | Returns |
|---|---|---|---|
| `/healthz` | All | Liveness — process is up | 200 always if process responds |
| `/healthz/ready` | ControlPlane, AuditService | Readiness — MongoDB reachable | 200 if DB ping succeeds, else 503 |
| `/healthz/detail` | ControlPlane | Detailed status, authenticated | JSON with version, uptime, DB, RabbitMQ |
| `/gateway/healthz` | API Gateway | Liveness | 200 |
| `/gateway/healthz/ready` | API Gateway | Readiness — includes OIDC provider connectivity | 200 or 503 |

Wire them up as:

- **Liveness probes** → `/healthz` (or `/gateway/healthz`). Restart the container if it fails.
- **Readiness probes** → `/healthz/ready` (or `/gateway/healthz/ready`). Remove from load balancer if it fails.
- **Alerting** → `/healthz/ready` from an external prober. If it stays failing for > 2 minutes on any component, page.

The Gateway's readiness probe verifies OIDC provider connectivity. If your IdP goes down, the Gateway fails readiness — which is correct; nobody can sign in. Do not turn this off.

### `/healthz/detail` example

```bash
curl -fs http://localhost:5100/healthz/detail \
    -H "X-Internal-Token: $(internal-jwt)" | jq
```

```json
{
  "version": "0.5.2",
  "uptime": "02:14:37",
  "dependencies": {
    "mongodb": { "status": "healthy", "latencyMs": 2 },
    "rabbitmq": { "status": "healthy", "latencyMs": 4 }
  }
}
```

## Prometheus `/metrics`

The AuditService exposes `/metrics` on port `:5200`. Scrape it from Prometheus:

```yaml
scrape_configs:
  - job_name: clustral-audit
    scrape_interval: 15s
    static_configs:
      - targets: ['audit-service:5200']
```

If you only expose the Gateway externally, route through it: `/audit-api/metrics` reaches the same endpoint. The Gateway enforces the internal-JWT auth on all `/audit-api/*` paths, so scrape from inside the network or add an unauthenticated exception for `/audit-api/metrics` on your reverse proxy.

The ControlPlane and API Gateway do not yet ship first-party Prometheus metrics. For those, rely on:

- **Structured logs** → log-to-metric pipeline (Loki with `metric` stages, Vector's `log_to_metric` transform, Fluent Bit's `prometheus_remote_write` output).
- **External HTTP probing** of health endpoints via blackbox_exporter.
- **RabbitMQ queue depth** for downstream backpressure signals.

## Structured logs

Every .NET service writes Serilog JSON to stdout. Each line carries the correlation ID, the trace context, and the component name.

```json
{
  "@t": "2026-04-15T12:03:11.204Z",
  "@l": "Information",
  "@mt": "{UserId} issued kubeconfig credential {CredentialId} for cluster {ClusterId}",
  "UserId": "...",
  "CredentialId": "...",
  "ClusterId": "...",
  "CorrelationId": "8f7e...",
  "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "SpanId": "00f067aa0ba902b7",
  "SourceContext": "Clustral.ControlPlane.Features.Auth.KubeconfigCredentialHandler",
  "Application": "Clustral.ControlPlane"
}
```

Key fields:

| Field | Use |
|---|---|
| `@t` | ISO 8601 timestamp (UTC). |
| `@l` | Level — `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`. |
| `@mt` | Message template with `{Placeholders}` — searchable without string matching. |
| `CorrelationId` | Per-request ID. Echoed in the `X-Correlation-Id` response header. |
| `TraceId` / `SpanId` | W3C Trace Context — pair with `traceparent`/`traceresponse` on the wire. |
| `SourceContext` | Typically the fully-qualified class name emitting the log. |
| `Application` | Component name — `Clustral.ControlPlane`, `Clustral.ApiGateway`, `Clustral.AuditService`. |

## What to alert on

Opinionated starter set. Adjust thresholds to your traffic shape, but do not drop the Page-severity signals.

| Signal | Threshold | Severity | Why |
|---|---|---|---|
| `/healthz/ready` fails for > 2 min on any component | 2 min | Page | Downtime on interactive traffic. |
| Agent `last_seen_at` > 5 min | 5 min | Warn; Page after 30 min | Target cluster unreachable to users. |
| 5xx rate on API Gateway | > 1% of a 5 min window | Page | User-facing errors. |
| OIDC provider unreachable (gateway readiness 503 with OIDC subsystem flag) | Any | Page | Nobody can sign in. |
| RabbitMQ queue depth on audit events | > 10k | Warn | Audit backlog; not user-impacting but a blind spot builds up. |
| MongoDB oplog lag (if replicated) | > 30s | Warn | Replication falling behind. |
| Credential issuance rate spike | > 3σ above 24h baseline | Warn | Possible abuse; inspect the audit log for the triggering user. |
| Access-request approval rate drop | > 50% below baseline | Info | May indicate admin unavailability. |
| `kubectl` proxy 429 rate | > 0.1% of 5 min window | Warn | Rate limits biting legitimate traffic; review `Proxy:RateLimiting`. |
| Internal JWT validation failures | > 0 sustained | Page | Key mismatch between Gateway and ControlPlane — traffic is broken. |

Agent `last_seen_at` comes from the `Cluster.LastHeartbeatAt` field and is exposed in `clustral clusters list`. Build a probe around it:

```bash
clustral clusters list --output json | \
    jq '.[] | select((now - (.lastHeartbeatAt | fromdateiso8601)) > 300)'
```

## Correlation ID tracing

Every HTTP response carries `X-Correlation-Id`. Use it to stitch events across components.

A user reports that `kubectl get pods` failed at 12:03 UTC:

1.  Get the correlation ID from their error message (plain-text errors on `/api/proxy/*` include it in the `X-Correlation-Id` response header; RFC 7807 responses embed it in the body too).
2.  Search each component's logs:
    ```bash
    docker compose logs api-gateway controlplane audit-service \
        --since 15m | jq 'select(.CorrelationId == "8f7e...")'
    ```
3.  Narrow the audit log by the actor and a 5-minute window around the event, then inspect the results for the correlation ID (the audit API does not currently filter by correlation ID):
    ```bash
    clustral audit --user alice@example.com --from 2026-04-15T12:00:00Z --to 2026-04-15T12:10:00Z
    ```

You get the full chain: Gateway auth decision → ControlPlane impersonation resolution → tunnel frame → audit event.

## W3C Trace Context

Clustral emits `traceparent` and `traceresponse` headers on every HTTP hop and every gRPC call. If you run a distributed tracing backend (Jaeger, Tempo, Grafana Cloud Traces), feed the emitted spans through the OpenTelemetry Collector for full request-hop visibility.

The gateway passes the incoming `traceparent` through unchanged when one is present, or creates a new span when one is not. The ControlPlane and AuditService pick up the context from `traceparent` and link their spans to the caller.

Minimal OTel Collector pipeline:

```yaml
receivers:
  otlp:
    protocols:
      grpc: {}
      http: {}
exporters:
  otlphttp/tempo:
    endpoint: https://tempo.example.com/otlp
service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [otlphttp/tempo]
```

Point each component's `OTEL_EXPORTER_OTLP_ENDPOINT` at the collector.

## Log aggregation

Serilog writes to stdout, so any container-native log collector picks it up.

- **Docker / Compose:** `docker logs` → Vector or Fluent Bit → Loki / Elasticsearch / CloudWatch Logs.
- **Kubernetes:** DaemonSet (Vector, Fluent Bit, Fluentd, Promtail) reading `/var/log/pods/*` → your log store.
- **systemd:** `journald` → `journal-upload`, Vector's `journald` source, or `systemd-journal-remote`.

Parse as JSON — do not treat the messages as unstructured. Most collectors auto-detect JSON on stdout; verify by grepping for a `@mt` key in a downstream sample.

## Dashboards — starter set

Build these six for a complete operational picture:

1.  **Per-component health overview** — one row per component with current status (`/healthz/ready`), uptime, and version. One glance tells you what's up.
2.  **Proxy request rate and latency** — request rate and p50/p99 latency for `/api/proxy/*`, broken down by cluster. Spikes and tail-latency regressions surface here first.
3.  **Access-request lifecycle funnel** — created → approved / denied / expired → revoked, over time. Approval lag is visible as the area between "created" and "approved" lines.
4.  **Credential issuance and revocation timeline** — kubeconfig JWTs issued vs. revoked per hour, per user cohort. Spot unusual issuance bursts.
5.  **Audit-event ingress lag** — RabbitMQ queue depth on the audit queue + AuditService consume rate. The area under the depth line is your tolerance for audit blindness.
6.  **Agent connection heatmap** — one row per cluster, one column per minute, colored by `AgentConnected` / `AgentDisconnected` state. Cluster-level flaps stand out.

## See also

- [Upgrade](upgrade.md) — watch these metrics during a rolling upgrade.
- [Troubleshooting](troubleshooting.md) — symptom-driven diagnostics that use the signals on this page.
- [Audit Log](../security-model/audit-log.md) — how the AuditService persists events and how to query them.
- [Security Model](../security-model/README.md) — what the health signals above are protecting.
