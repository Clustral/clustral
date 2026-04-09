# Clustral.E2E.Tests — Claude Code Guide

End-to-end test suite that exercises the full production path:

```
kubectl → ControlPlane REST → gRPC tunnel → Go Agent → real K3s API
```

Every component is the **real production binary**, run as a Docker container
on a shared Docker network. There are no mocks, no in-process shortcuts, and
no test-only auth handlers — Keycloak issues real OIDC tokens, the
ControlPlane validates real JWTs, and the Go agent forwards real
multi-value impersonation headers to a real Kubernetes API.

The cost is wall-clock time (~60s of fixture startup, image builds on first
run). The benefit is that this suite catches regressions the in-process
integration tests cannot — most importantly the .NET-vs-Go header behaviour
that motivated the Go agent rewrite.

---

## When to add an E2E test

Add a test here when a change touches the **agent ↔ ControlPlane tunnel**,
the **kubectl proxy path**, or the **credential lifecycle**. Concretely:

- Changes to `KubectlProxyMiddleware`, `ProxyAuthService`, or `ImpersonationResolver`
- New gRPC methods on `TunnelService`, `ClusterService`, or `AuthService`
- Changes to the Go agent (`src/clustral-agent/`)
- Changes to mTLS, JWT, or credential rotation logic
- New fields on `Cluster`, `AccessRequest`, or `AccessToken` that affect proxy auth

If the change is purely ControlPlane-internal (e.g., a new query, a domain
event, an admin REST endpoint), the existing integration tests in
`Clustral.ControlPlane.Tests/` are sufficient.

---

## Project layout

```
Clustral.E2E.Tests/
├── Clustral.E2E.Tests.csproj
├── Fixtures/
│   ├── E2EFixture.cs            ← shared fixture: builds images, starts containers
│   ├── E2ETestCollection.cs     ← xUnit collection so all tests share E2EFixture
│   ├── E2ETestContext.cs        ← per-test setup helper (cluster + agent + role + credential)
│   ├── KeycloakTokenClient.cs   ← OIDC password-grant token acquisition
│   ├── ControlPlaneClient.cs    ← typed REST API wrapper
│   └── AgentLogReader.cs        ← polls container logs for renewal events
└── Tests/
    ├── AgentBootstrapTests.cs       ← register cluster → agent connects → tunnel up
    ├── KubectlProxyTests.cs         ← real kubectl through tunnel → real K3s response
    ├── RoleBasedAccessTests.cs      ← multi-value impersonation header forwarding
    ├── AgentReconnectionTests.cs    ← tunnel teardown and recovery
    ├── CredentialLifecycleTests.cs  ← issue → use → revoke → 401
    └── AgentRenewalTests.cs         ← cert and JWT renewal under aggressive thresholds
```

---

## Architecture

```
Docker network: clustral-e2e-{guid}

┌──────────┐  ┌────────┐  ┌──────────────┐  ┌─────────┐  ┌─────┐
│ Keycloak │  │ Mongo  │  │ ControlPlane │  │  Agent  │  │ K3s │
│  :8080   │  │ :27017 │  │ REST :5100   │  │  (Go)   │──│:6443│
│          │◀─│        │◀─│ gRPC :5443   │◀─│         │  │     │
└──────────┘  └────────┘  └──────────────┘  └─────────┘  └─────┘
                                  ▲
                                  │ HTTP (REST)
                          ┌───────┴───────┐
                          │   xUnit test  │
                          └───────────────┘
```

**Container responsibilities:**

| Container | Source | Network alias | Purpose |
|---|---|---|---|
| `mongo` | `mongo:8` (Testcontainers) | `mongo` | ControlPlane data store |
| `keycloak` | `quay.io/keycloak/keycloak:24.0` | `keycloak` | Real OIDC provider, imports `infra/keycloak/clustral-realm.json` |
| `k3s` | `rancher/k3s` (Testcontainers.K3s, privileged) | `k3s` | Real Kubernetes API |
| `controlplane` | built from `src/Clustral.ControlPlane/Dockerfile` | `controlplane` | Production ControlPlane image |
| Per-test agent | built from `src/clustral-agent/Dockerfile` | (per-test) | Real Go binary |

The ControlPlane and Agent images are built **once per test run** during
`E2EFixture.InitializeAsync` and reused. Per-test agent containers are
spawned via `E2EFixture.StartAgentAsync` so each test gets a clean cluster
and isolated state.

---

## Key design decisions

### Real Keycloak with the production realm

`E2EFixture` mounts `infra/keycloak/clustral-realm.json` into Keycloak via
`WithResourceMapping` and runs `start-dev --import-realm`. Tests get JWT
tokens from the real OIDC token endpoint via the resource owner password
grant — no test auth bypass anywhere.

This requires the `clustral-cli` realm client to have
`directAccessGrantsEnabled: true`. That flag is enabled in the committed
realm export so tests can authenticate without a browser flow.

### Why the C# mock-agent approach was rejected

An earlier draft used a C# class implementing the gRPC tunnel protocol and
forwarding requests to K3s via `HttpClient`. That approach was abandoned
because **`HttpClient` collapses multi-value headers into a single comma-
separated line**, which is exactly the bug the Go agent was rewritten to
fix. A C# mock would mask the bug instead of catching regressions in it.
This suite uses the real Go binary built from `src/clustral-agent/Dockerfile`.

### The `AgentAuthInterceptor` runs the real mTLS path

In the in-process integration tests, `AgentAuthInterceptor` skips its checks
because `httpContext.Connection.LocalPort != 5443`. In E2E tests Kestrel is
listening on the real `:5443` port, so the interceptor enforces the full
mTLS + JWT bootstrap path. The `CertificateAuthority__CaCertPath` and
`CaKeyPath` env vars point at a CA generated by `E2EFixture.GenerateTestCA`
and mounted into the container.

### Per-test isolation

Every test class registers its own cluster (with a unique name) and starts
its own agent container. The ControlPlane and infrastructure containers are
shared across the whole test run via `[CollectionDefinition("E2E")]`.
Tests should never depend on state created by other tests.

### Aggressive renewal config in `AgentRenewalTests`

`AgentRuntimeOptions.AggressiveRenewal` sets:

```
AGENT_RENEWAL_CHECK_INTERVAL=2s
AGENT_CERT_RENEW_THRESHOLD=8760h
AGENT_JWT_RENEW_THRESHOLD=8760h
```

The agent's `RenewalManager` checks every 2 seconds and treats anything
expiring in less than a year as "expires soon", forcing both cert and JWT
renewal on the very first tick. Combined with the ControlPlane container's
`CertificateAuthority__ClientCertValidityDays=1` and `JwtValidityDays=1`,
the renewal codepath fires within a few seconds of agent startup.

`AgentLogReader.WaitForLogLineAsync` polls container stdout for the exact
log lines emitted by the Go agent
(`internal/auth/renewal.go:118` and `:143`).

---

## Running the tests

```bash
# All E2E tests
dotnet test src/Clustral.E2E.Tests

# Single scenario
dotnet test src/Clustral.E2E.Tests \
  --filter "FullyQualifiedName~KubectlProxyTests"

# Skip from a regular CI run
dotnet test Clustral.slnx --filter "Category!=E2E"
```

**Requirements:**

- Docker (or OrbStack) running
- Privileged container support — K3s needs it
- ~2 GB free RAM for the container stack
- ~60 s for the first run (image builds + Keycloak warmup)
- Subsequent runs reuse cached layers

If a test hangs at "ControlPlane did not become ready" or
"cluster did not reach status Connected", it usually means a container
failed to start. Check `docker logs` on the container or use
`AgentHandle.DumpLogsAsync()` from inside the test (already wired into
catch blocks for the bootstrap flow).

---

## Adding a new E2E test

1. Pick the right test class — or create a new one if your scenario
   doesn't fit the existing categories.
2. Add `[Collection(E2ETestCollection.Name)]` and
   `[Trait("Category", "E2E")]` to the class.
3. Use `E2ETestContext.SetupAsync(...)` for the boilerplate (cluster,
   agent, role, credential). It returns an `IAsyncDisposable` that cleans
   up the agent container on test completion.
4. Use `FluentAssertions` (`.Should()...`) for all assertions.
5. Use `ITestOutputHelper` to log diagnostics.
6. Wrap any `WaitFor*Async` call in a try/catch that dumps the agent logs
   on `TimeoutException`. Renewal and tunnel failures otherwise look like
   silent timeouts.

```csharp
[Fact]
public async Task NewScenario()
{
    await using var ctx = await E2ETestContext.SetupAsync(
        fixture, output, k8sGroups: new[] { "system:masters" });

    using var response = await ctx.Cp.KubectlGetAsync(
        ctx.ClusterId, ctx.CredentialToken, "/api/v1/services");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

---

## Things the suite does NOT cover

- **Load and concurrency** — these tests are functional, not perf.
- **Cross-cluster scenarios** — every test uses a single K3s instance.
- **Web UI** — Next.js is not started. Web tests live in `src/Clustral.Web`.
- **CLI binary** — the .NET CLI is not invoked; tests call the REST API
  directly via `ControlPlaneClient`. CLI behaviour is covered by
  `Clustral.Cli.Tests`.
