# Clustral.AuditService — Claude Code Guide

ASP.NET Core web host that persists and serves the platform's audit log. It
exposes:

- **REST API** on `:5200` — consumed by the CLI (`clustral audit`) and the
  Web UI (via Next.js proxy). Not directly exposed: the API Gateway proxies
  `/audit-api/*` requests and strips the prefix.
- **Swagger UI** at `http://localhost:5200/swagger` (dev only).
- **MassTransit consumer pool** — subscribes to integration events on
  RabbitMQ and writes a matching `AuditEvent` row for each one.

No gRPC. No inbound agent traffic. No Kubernetes-adjacent surface. All
requests come through the gateway carrying an `X-Internal-Token` (ES256
internal JWT) — the gateway has already validated the external OIDC token.

---

## Directory map

```
Clustral.AuditService/
├── Program.cs                       ← Host setup (DI, middleware, routing)
├── appsettings.json                 ← Default config
├── appsettings.Development.json     ← Dev overrides
│
├── Api/
│   ├── Controllers/
│   │   └── AuditController.cs       ← GET /api/v1/audit (list),
│   │                                    GET /api/v1/audit/{uid} (single)
│   └── AuditListValidator.cs        ← FluentValidation for list query params
│
├── Domain/
│   ├── AuditEvent.cs                ← Aggregate (append-only, static Create factory)
│   ├── EventCodes.cs                ← Canonical event-code catalog (Teleport-style)
│   └── Repositories/
│       └── IAuditEventRepository.cs ← Insert / Find / List contract
│
├── Consumers/                       ← One MassTransit consumer per integration event.
│   ├── AccessRequestApprovedConsumer.cs
│   ├── AccessRequestCreatedConsumer.cs
│   ├── AccessRequestDeniedConsumer.cs
│   ├── AccessRequestExpiredConsumer.cs
│   ├── AccessRequestRevokedConsumer.cs
│   ├── AgentAuthFailedConsumer.cs
│   ├── ClusterEventConsumers.cs     ← Registered / Connected / Disconnected / Deleted
│   ├── CredentialIssueFailedConsumer.cs
│   ├── CredentialIssuedConsumer.cs
│   ├── CredentialRevokeDeniedConsumer.cs
│   ├── CredentialRevokedConsumer.cs
│   ├── ProxyAccessDeniedConsumer.cs
│   ├── ProxyRequestCompletedConsumer.cs
│   ├── RoleEventConsumers.cs        ← Created / Updated / Deleted
│   └── UserEventConsumers.cs        ← Synced / RoleAssigned / RoleUnassigned
│
├── Features/
│   └── Audit/
│       └── Queries/
│           ├── ListAuditEvents.cs   ← Query handler with filters + pagination
│           └── GetAuditEvent.cs     ← By-uid lookup
│
└── Infrastructure/
    ├── AuditDbContext.cs             ← MongoDB typed collection + index setup
    └── MongoAuditEventRepository.cs  ← IAuditEventRepository implementation
```

---

## Key design decisions

### Append-only audit log
`AuditEvent` is immutable after creation — every field has `{ get; init; }`.
There is no update command, no state-transition method, no soft-delete flag.
Audit logs are the system-of-record for "what happened" and must be
tamper-resistant by construction. Use `AuditEvent.Create(...)` instead of
a raw constructor so the factory can enforce invariants (non-empty
`event`/`code`/`category`).

### Event codes follow a Teleport-style catalog
Every event code in `Domain/EventCodes.cs` follows `[PREFIX][NUMBER][SEVERITY]`:

| Prefix | Domain |
|---|---|
| `CAR` | Access Requests |
| `CCR` | Credentials |
| `CCL` | Clusters |
| `CRL` | Roles |
| `CUA` | User Auth |
| `CAG` | Agent Auth |
| `CPR` | Proxy (kubectl) |

Severity suffix: `I`=Info, `W`=Warning, `E`=Error. **Always add new codes
to this catalog and to the `All[]` array** so validation and uniqueness
checks keep working. Never inline a string-literal event code in a
consumer.

### Event-driven — no direct DB writes from other services
The ControlPlane never talks to the audit database directly. It publishes
integration events on RabbitMQ via MassTransit (`Clustral.Contracts/
IntegrationEvents/`); every event has a dedicated consumer in
`Consumers/` that translates the domain event into an `AuditEvent` row.
This keeps the audit trail loosely coupled, survives AuditService
downtime (RabbitMQ queues buffer), and centralizes persistence shape.

One consumer class per integration event. Named `{EventName}Consumer`.
Signature: `public async Task Consume(ConsumeContext<TEvent> context)`.

### Authentication — Internal JWT only
AuditService validates ES256 internal JWTs issued by the API Gateway.
Tokens arrive in the `X-Internal-Token` header. The public key is
mounted at `InternalJwt:PublicKeyPath`. No OIDC fallback, no direct
access — if the gateway is bypassed, requests get 401.

Uses `Clustral.Sdk.Auth.InternalJwtService.ForValidation(publicKeyPem)`.
`[Authorize]` on `AuditController` enforces the requirement.

### Persistence — MongoDB
`AuditDbContext` wraps one collection (`audit_events`) with six indexes
covering the common query paths:
- `{ Time DESC }` — newest-first scans
- `{ Category, Time DESC }` — category filter
- `{ User, Time DESC }` — per-user audit trail
- `{ Code }` — code exact match
- `{ ClusterId, Time DESC }` — per-cluster view
- `{ ResourceId, Time DESC }` — per-resource history

No EF Core. No migrations. Indexes are created once on startup via
`EnsureIndexesAsync()` (idempotent).

---

## Result Pattern

Queries use `Result<T>` from `Clustral.Sdk.Results`. The controller returns
`result.ToActionResult()`; errors surface as RFC 7807 Problem Details via
`Clustral.Sdk.Http.ProblemDetailsWriter`.

## Error shapes

AuditService uses **RFC 7807 Problem Details exclusively** — it's a
general-purpose REST API consumed by the Web UI and CLI, never by kubectl.
The proxy path's `v1.Status` shape is ControlPlane-specific.

- **Validation errors** — `AuditListValidator` (FluentValidation) runs in
  the controller. Failures produce `ResultError.Validation(...)` →
  `ToActionResult()` → RFC 7807 **422** with `code: VALIDATION_ERROR` and
  the offending `field`. **Never** use `return BadRequest("plain text")` —
  it bypasses the shape contract.
- **Unhandled exceptions** — `Clustral.Sdk.Http.GlobalExceptionHandlerMiddleware`
  (registered first) catches them and writes RFC 7807.
- **Every response** echoes `X-Correlation-Id` via
  `Clustral.Sdk.Http.CorrelationIdMiddleware`.

See `docs/adr/001-error-response-shapes.md` for the rationale and the root
README for wire examples + canonical error-code table.

---

## Central Package Management

Package versions are centralised in `Directory.Packages.props` at the
repository root. Use `<PackageReference Include="Foo" />` **without** a
`Version="..."` attribute — the version is resolved from the props file.

---

## Common tasks

### Run the service locally

```bash
docker compose -f infra/docker-compose.yml up -d      # Mongo + RabbitMQ
dotnet run --project src/Clustral.AuditService
# → http://localhost:5200
```

### Inspect persisted audit events

```bash
docker exec -it clustral-mongo-1 mongosh \
  --eval 'db.getSiblingDB("clustral-audit").audit_events.find().sort({Time:-1}).limit(10).pretty()'
```

### List audit events via the REST API

```bash
# Through the gateway (requires a valid OIDC token)
curl -H "Authorization: Bearer $(cat ~/.clustral/token)" \
     "https://$HOST_IP/audit-api/api/v1/audit?category=credentials&pageSize=20"
```

The CLI wraps this: `clustral audit --category credentials --limit 20`.

---

## Adding a new audit event

1. **Add an integration event** to `packages/Clustral.Contracts/IntegrationEvents/`
   (e.g., `FooBarEvent.cs` — `public sealed record FooBarEvent(Guid UserId, ...)`).
2. **Publish it from the ControlPlane** in the domain-event handler that
   owns the source operation. Enrich it with email/cluster name there —
   AuditService should not call ControlPlane to resolve IDs.
3. **Add a code to `Domain/EventCodes.cs`** following the prefix
   conventions (update the `All[]` array). Pick the right severity suffix.
4. **Create a consumer in `Consumers/`** named `FooBarConsumer`:
   ```csharp
   public sealed class FooBarConsumer(
       IAuditEventRepository repository,
       ILogger<FooBarConsumer> logger) : IConsumer<FooBarEvent>
   {
       public async Task Consume(ConsumeContext<FooBarEvent> context)
       {
           var evt = context.Message;
           var auditEvent = AuditEvent.Create(
               @event: "foo.bar",
               code: EventCodes.FooBar,
               category: "foo",
               severity: Severity.Info,
               success: true,
               time: evt.OccurredAt,
               // … map remaining fields
               metadata: evt.ToBsonDocument());
           await repository.InsertAsync(auditEvent);
           logger.LogInformation("Audit [{Code}] {Event}: {Message}",
               auditEvent.Code, auditEvent.Event, auditEvent.Message);
       }
   }
   ```
   The consumer is auto-registered by `AddMassTransitWithRabbitMq(..., consumersAssembly: typeof(Program).Assembly)`.
5. **Write unit tests** in `Clustral.AuditService.Tests/Consumers/` —
   assert the event is persisted with the correct code, category, severity,
   and fields. Use `AuditConsumerTestBase` if it exists, otherwise follow
   the pattern in `CredentialEventConsumerTests`.
6. **Use FluentAssertions** in all tests (`.Should().Be(...)`).
7. **Do NOT** consume events that don't have a contract — if you need to
   audit something new, publish an integration event from the source service
   first. Tight coupling via shared DB is forbidden.

---

## Adding a new query endpoint

1. Create `Features/Audit/Queries/YourQuery.cs` — a query record
   implementing `IQuery<Result<YourResponse>>` and a handler.
2. Add an action to `AuditController` that calls
   `mediator.Send(new YourQuery(...))` and returns `result.ToActionResult()`.
3. If it takes input that needs validation, add a FluentValidation
   validator in `Api/` (same pattern as `AuditListValidator`) and call it
   from the controller. Do not use `[Required]` data annotations.
4. Write integration tests in `Clustral.AuditService.Tests/Api/` using
   `WebApplicationFactory` + a seeded MongoDB (Testcontainers).
5. Use **FluentAssertions** everywhere.

---

## Testing

Tests live in `src/Clustral.AuditService.Tests/`:

- **Consumer tests** (`Consumers/*Tests.cs`) — publish an integration
  event, assert the correct `AuditEvent` is persisted. Use Testcontainers
  MongoDB + a MassTransit in-memory test harness.
- **API tests** (`Api/*Tests.cs`) — `WebApplicationFactory` spin-up with
  a seeded collection. Assert status codes, response shape (RFC 7807 on
  error paths), and that `X-Correlation-Id` is echoed.
- **Domain tests** (`Domain/*Tests.cs`) — `AuditEvent.Create` invariants
  and `EventCodes.All` uniqueness.

Run:

```bash
dotnet test src/Clustral.AuditService.Tests --filter "Category!=E2E"
```

---

## Security-sensitive paths

| Location | Risk |
|---|---|
| `InternalJwt` validation (`Program.cs`) | Only barrier between the gateway and the audit API. A bug here = anonymous read of the full audit log. |
| `AuditEvent.Create` invariants | Missing or lax validation lets malformed events pollute the log. |
| `IAuditEventRepository.InsertAsync` | Must not silently drop on insert error — audit gaps are worse than duplicates. |

---

## Things to implement next

| # | What | Where |
|---|---|---|
| 1 | Audit log retention policy (TTL + background deletion) | New hosted service + `audit_events` TTL index |
| 2 | Export to SIEM (syslog / Splunk / Elastic via an exporter consumer) | New `Exporters/` folder parallel to `Consumers/` |
| 3 | Per-user timeline endpoint (`GET /api/v1/audit/users/{id}/timeline`) | New query + controller action |
| 4 | `AuditService` health check for MongoDB readiness | `Program.cs` + `/healthz/ready` |
