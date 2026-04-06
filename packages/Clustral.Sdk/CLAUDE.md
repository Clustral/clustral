# Clustral.Sdk — Claude Code Guide

Shared class library consumed by all four Clustral components
(`ControlPlane`, `Agent`, `Cli`, `Web` indirectly via the REST API).
Contains the three utility classes listed below, plus the generated gRPC stubs
for all three proto contracts.

---

## Class reference

### `Clustral.Sdk.Auth.TokenCache`

**File:** `Auth/TokenCache.cs`

Reads and writes the user's JWT to `~/.clustral/token`.

| Method | Description |
|---|---|
| `StoreAsync(token)` | Creates the directory if needed, then writes the token atomically. |
| `ReadAsync()` | Returns the stored token, or `null` if the file does not exist. |
| `ClearAsync()` | Deletes the token file. Used by `clustral logout`. |

**Locking strategy:** combines a `SemaphoreSlim` (in-process) with
`FileStream` opened as `FileShare.None` (OS-level) so that concurrent CLI
invocations cannot corrupt the file. A retry loop with 50 ms back-off
handles brief cross-process contention.

**Do not** store any other credentials or secrets here; the file is plain-text
on disk.

---

### `Clustral.Sdk.Kubeconfig.KubeconfigWriter`

**Files:** `Kubeconfig/KubeconfigWriter.cs`, `Kubeconfig/KubeconfigDocument.cs`

Parses `~/.kube/config` (or `$KUBECONFIG`) with YamlDotNet, upserts the
Clustral cluster/user/context triple, then writes the file back.

| Method | Description |
|---|---|
| `WriteClusterEntry(entry, setCurrentContext)` | Upserts all three entries. Optionally sets `current-context`. |
| `RemoveClusterEntry(contextName)` | Removes the entries for the given context name. Falls back `current-context` to the first remaining context. |

**`ClustralKubeconfigEntry`** is the input record:

```csharp
new ClustralKubeconfigEntry(
    ContextName:           "clustral-prod",
    ServerUrl:             "https://cp.example.com/proxy/prod",
    Token:                 "<short-lived bearer token>",
    ExpiresAt:             DateTimeOffset.UtcNow.AddHours(8),
    InsecureSkipTlsVerify: false   // true only for kind / local dev
);
```

**YamlDotNet and NativeAOT:** YamlDotNet uses reflection-based serialization.
Before publishing `Clustral.Cli` with NativeAOT (`dotnet publish -r osx-arm64`),
check trimmer warnings on this class.  If warnings appear, add
`[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(KubeconfigDocument))]`
on the `KubeconfigWriter` constructor, or migrate to a source-generated YAML
approach.

**Enterprise features:** KubeconfigWriter supports deterministic ordering of
kubeconfig entries, validation of cluster/user/context triples before writing,
security policies (e.g. disallowing `insecure-skip-tls-verify` in production),
credential redaction in logs, multiple auth modes (certificate, exec-based,
token), and configurable merge strategies for multi-file `$KUBECONFIG` setups.

---

### `Clustral.Sdk.Results` — Result Pattern

**Files:** `Results/Result.cs`, `Results/ResultError.cs`, `Results/ResultErrors.cs`,
`Results/ResultExtensions.cs`, `Results/ResultFailureException.cs`

A railway-oriented error-handling pattern used across the ControlPlane and Sdk
instead of throwing exceptions for domain-level failures.

| File | Description |
|---|---|
| `Results/Result.cs` | `Result<T>` immutable struct with `Map`, `MapAsync`, `Ensure`, `Match`, and `ThrowIfFailed` combinators. |
| `Results/ResultError.cs` | Error record with `Kind`, `Code`, `Message`, `Field`, `TraceId`, and `Metadata` properties. |
| `Results/ResultErrors.cs` | Domain error catalog — pre-defined errors such as `ClusterNotFound`, `RoleNotFound`, `AccessDenied`, etc. |
| `Results/ResultExtensions.cs` | ASP.NET Core integration — `ToActionResult` (RFC 7807 Problem Details), `ToCreatedResult`, `ToGrpcResult`. |
| `Results/ResultFailureException.cs` | Bridge to the exception-handler middleware for cases where a `Result` failure must propagate as an exception. |

**Usage pattern:**

```csharp
Result<Cluster> result = await clusterService.GetByIdAsync(id);
return result.ToActionResult();   // 200 with body, or RFC 7807 error
```

---

### `Clustral.Sdk.Grpc.GrpcChannelFactory`

**Files:** `Grpc/GrpcChannelFactory.cs`, `Grpc/BearerTokenHandler.cs`

Creates `GrpcChannel` instances with bearer-token authentication baked in via
a `DelegatingHandler` (`BearerTokenHandler`). The handler re-reads the token
on **every** HTTP/2 request, so a silent token rotation (new token written by
`TokenCache.StoreAsync`) is picked up without recreating the channel.

| Factory method | When to use |
|---|---|
| `CreateAuthenticatedChannelAsync(address)` | Production. Reads token from `TokenCache` at construction; fails fast if no token exists. |
| `CreateWithToken(address, token)` | Token already in memory (e.g. just issued). Avoids a disk round-trip. |
| `CreateInsecureWithToken(address, token)` | Local dev against kind / docker-compose (HTTP, no TLS). Sets the `Http2UnencryptedSupport` switch process-wide. |

**Channel lifetime:** channels are expensive — create once and reuse for the
lifetime of the host/command.  Dispose on shutdown.

---

## Generated gRPC stubs

Stubs for `ClusterService`, `TunnelService`, and `AuthService` are generated at
build time by `Grpc.Tools` from the `.proto` files in `packages/proto/`.
They live in `obj/` and are **not committed**.  Run `dotnet build` to regenerate.

See `packages/proto/README.md` for full regeneration instructions.

---

## Adding a new shared utility

1. Place it under a descriptive subdirectory (`Auth/`, `Kubeconfig/`, `Grpc/`, `Results/`, etc.).
2. If the CLI will call it, audit for NativeAOT compatibility (no unbounded
   reflection, no `dynamic`, no `Activator.CreateInstance` without a `[DynamicDependency]`).
3. **Write unit tests** in `Clustral.Sdk.Tests` — at minimum happy-path + error-path coverage. Use `ITestOutputHelper` for test output.
4. Update this file.

---

## Central Package Management

NuGet package versions are managed centrally via `Directory.Packages.props` at
the repository root. Do not specify `Version` attributes in individual `.csproj`
files — use `<PackageReference Include="..." />` without a version, and add or
update the version in `Directory.Packages.props` instead.
