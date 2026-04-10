# Clustral.Cli — Claude Code Guide

NativeAOT console application that gives developers a `clustral` binary for
authenticating with Keycloak and writing kubeconfig credentials.

---

## Command tree

```
clustral
├── login [controlplane-url]        ← OIDC PKCE flow
├── logout                          ← Revoke all credentials, clear JWT
├── kube
│   ├── login <cluster>              ← Issue kubeconfig credential (name or GUID)
│   ├── logout <cluster>            ← Revoke credential, remove context
│   └── list (alias: ls)            ← List available clusters
├── clusters
│   └── list (alias: ls)            ← List registered clusters
├── users
│   └── list (alias: ls)            ← List all users
├── roles
│   └── list (alias: ls)            ← List all roles
├── access
│   ├── request                     ← Request JIT access
│   ├── list (alias: ls)            ← List access requests
│   ├── approve <id>                ← Approve pending request
│   ├── deny <id>                   ← Deny pending request
│   └── revoke <id>                 ← Revoke active grant
├── config
│   ├── show [--json] [--remote]    ← Show CLI files, session, kubeconfig
│   ├── path                        ← Print file paths the CLI uses
│   └── clean [--yes] [--dry-run]   ← Factory reset (delete all CLI state)
├── status                          ← Session, clusters, grants, ControlPlane health
├── doctor                          ← Sequential connectivity diagnostics
├── profiles
│   ├── list (alias: ls)            ← List profiles (default always first)
│   ├── use <name>                  ← Switch active profile (or "default")
│   ├── current                     ← Show active profile name
│   ├── create <name>               ← Create a new profile
│   └── delete <name>               ← Delete a profile (default protected)
├── accounts
│   ├── list (alias: ls)            ← List accounts in current profile
│   ├── use <email>                 ← Switch active account
│   └── remove <email>              ← Remove a stored account
├── whoami                          ← Quick identity + session validity check
├── completion <shell>              ← Generate bash/zsh/fish completions
├── update                          ← Self-update from GitHub
└── version                         ← Show CLI version
```

---

## File map

```
Clustral.Cli/
├── Program.cs                    ← Builds the root command and invokes System.CommandLine
│
├── Commands/
│   ├── LoginCommand.cs           ← clustral login
│   ├── KubeLoginCommand.cs       ← clustral kube / clustral kube login
│   ├── KubeLogoutCommand.cs      ← clustral kube logout
│   ├── UsersCommand.cs           ← clustral users list
│   ├── RolesCommand.cs           ← clustral roles list
│   ├── AccessCommand.cs          ← clustral access (request, list, approve, deny, revoke)
│   ├── ConfigCommand.cs          ← clustral config show / path (offline introspection)
│   ├── StatusCommand.cs          ← clustral status (session, clusters, grants, health)
│   ├── DoctorCommand.cs          ← clustral doctor (connectivity diagnostics)
│   ├── ProfileCommand.cs         ← clustral profile (create/use/list/current/delete + path resolution)
│   ├── AccountsCommand.cs        ← clustral accounts (list/use/remove per-profile accounts)
│   ├── WhoamiCommand.cs          ← clustral whoami (local JWT decode, no HTTP)
│   ├── CompletionCommand.cs      ← clustral completion bash/zsh/fish
│   └── NameResolver.cs           ← Shared cluster/role name → ID resolver (name or GUID)
│
├── Http/
│   ├── CliHttp.cs                ← Shared HTTP client factory + spinner helper + CliHttpErrorException
│   └── DebugLoggingHandler.cs    ← HTTP request/response tracing when --debug is on
│
├── Ui/
│   ├── CliErrors.cs              ← Flat error/warning display (● indicator + dim detail rows)
│   ├── CliExceptionHandler.cs    ← Global exception classifier (mirrors ControlPlane's GlobalExceptionHandlerMiddleware)
│   └── Messages.cs               ← Centralized user-facing string catalog
│
├── Auth/
│   ├── OidcFlowHandler.cs        ← PKCE orchestration (verifier, challenge, browser, exchange)
│   └── OidcCallbackServer.cs     ← HttpListener on 127.0.0.1:{port}/
│
└── Config/
    └── CliConfig.cs              ← ~/.clustral/config.json  +  AOT JSON context
                                    also houses wire types:
                                      KeycloakTokenResponse
                                      IssueCredentialRequest
                                      IssueCredentialResponse
```

---

## Login flow (`clustral login`)

```
LoginCommand.HandleAsync
  └── OidcFlowHandler.LoginAsync
        1. GenerateCodeVerifier / GenerateCodeChallenge  (SHA-256, base64url)
        2. GenerateState                                 (16 random bytes)
        3. BuildAuthorizationUrl                         (query string via HttpUtility)
        4. OpenBrowser                                   (Process.Start + UseShellExecute)
        5. OidcCallbackServer.WaitForCallbackAsync       (HttpListener, serves HTML page)
        6. Parse query: validate state, extract code
        7. ExchangeCodeAsync                             (POST to /protocol/openid-connect/token)
        └── returns access_token
  └── TokenCache.StoreAsync                             (writes ~/.clustral/token)
```

## kube login flow (`clustral kube login <cluster>`)

```
KubeLoginCommand.HandleAsync
  1. TokenCache.ReadAsync                               (reads ~/.clustral/token)
  2. NameResolver.ResolveClusterIdAsync                  (name → ID; GUID input skips HTTP)
  3. POST /api/v1/auth/kubeconfig-credential             (IssueCredentialRequest → IssueCredentialResponse)
  4. KubeconfigWriter.WriteClusterEntry                  (upserts ~/.kube/config)
     ContextName = clustral-<cluster>  (override with --context-name)
     ServerUrl   = {ControlPlaneUrl}/api/proxy/<resolved-cluster-id>
```

---

## Configuration

`~/.clustral/config.json` is loaded by `CliConfig.Load()` on every command
invocation.  All fields have CLI option overrides; command-line values take
precedence.

| Field | Default | CLI override |
|---|---|---|
| `OidcAuthority` | *(required)* | `--authority` |
| `OidcClientId` | `clustral-cli` | `--client-id` |
| `OidcScopes` | `openid email profile` | `--scopes` |
| `ControlPlaneUrl` | *(required)* | *(none — set in config)* |
| `CallbackPort` | `7777` | `--port` |
| `InsecureTls` | `false` | `--insecure` |

Example config file:

```json
{
  "oidcAuthority": "http://localhost:8080/realms/clustral",
  "oidcClientId": "clustral-cli",
  "oidcScopes": "openid email profile",
  "controlPlaneUrl": "http://localhost:5000",
  "callbackPort": 7777,
  "insecureTls": true
}
```

---

## NativeAOT constraints

| Area | Rule |
|---|---|
| JSON | All serialised types listed in `CliJsonContext`. No `JsonSerializer.Serialize<T>()` without source-gen context. |
| HTTP | Use `HttpClient` + `HttpClientHandler` directly. No `IHttpClientFactory` (requires DI). |
| Browser | `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` — best-effort. |
| Callback | `HttpListener` — AOT-safe on all platforms. |
| YamlDotNet | Used transitively via `KubeconfigWriter`. Covered by `<NoWarn>IL2026;IL3050</NoWarn>`. If trimmer warnings cause runtime failures add `[DynamicDependency]` on the call sites in `KubeLoginCommand`. |

---

## Input Validation

The CLI uses **FluentValidation** for all input validation. Validators live in
`Validation/` with input records, validator classes, and a shared helper.

```
Validation/
├── CommandInputs.cs         ← Input records: KubeLoginInput, AccessRequestInput, etc.
├── Iso8601Duration.cs       ← ISO 8601 duration validator + shorthand normalizer (8H → PT8H)
├── KubeLoginValidator.cs    ← cluster (name or GUID) + ttl (ISO 8601)
├── AccessRequestValidator.cs ← role + cluster (required) + duration
├── AccessActionValidator.cs  ← request-id (GUID) — used by approve/revoke
├── AccessDenyValidator.cs    ← request-id (GUID) + reason (required)
└── ValidationHelper.cs       ← Runs validator + displays errors + sets exit code
```

**Every new CLI command with non-trivial input must use FluentValidation.**
Do not use manual `string.IsNullOrWhiteSpace` checks for validation. Instead:

1. Create an input record in `CommandInputs.cs`.
2. Create a validator class extending `AbstractValidator<T>`.
3. Call `ValidationHelper.Validate(console, new FooValidator(), input, ctx)`
   before proceeding with HTTP calls or business logic.
4. Write unit tests for the validator.

Validators are instantiated directly (no DI) — `new FooValidator().Validate(input)`.

---

## Error Display

The CLI uses `Ui/CliErrors.cs` for consistent, card-style error output via
Spectre.Console panels. Five methods are available:

| Method | Purpose |
|---|---|
| `WriteHttpError` | Formats HTTP error responses (status code + body) into a bordered panel |
| `WriteConnectionError` | Displays connection failures (e.g. ControlPlane unreachable) with actionable hints |
| `WriteError` | General-purpose error panel for unexpected errors |
| `WriteNotConfigured` | Shown when `~/.clustral/config.json` is missing or incomplete; guides the user to run `clustral login` |
| `WriteValidationErrors` | Displays FluentValidation failures as a yellow-bordered card listing each field and its error |

---

## Spectre.Console

The CLI uses [Spectre.Console](https://spectreconsole.net/) for rich terminal
output including tables, panels, and markup formatting. The previous custom
`Ansi.cs` helper was removed in favour of Spectre.Console's built-in
capabilities.

---

## Testing

Every new CLI command or feature must include tests in `src/Clustral.Cli.Tests/`.
**All tests must use FluentAssertions** (`.Should().Be(...)`) — do not use
`Assert.Equal` or `Assert.True`.

- **Validator tests**: test FluentValidation rules (valid/invalid inputs, error messages, multi-field errors) in `Validation/`.
- **Command tree tests**: verify the command is registered with correct options/arguments.
- **Render tests**: use `TestConsole` from `Spectre.Console.Testing` to capture and assert on visual output via `ITestOutputHelper`.
- **Wire type tests**: verify JSON serialization round-trips for any new DTO types in `CliJsonContext`.
- **Error display tests**: test `CliErrors.*` card rendering for new error scenarios.

---

## Build & publish

```bash
# Standard build (validates AOT-safety rules):
dotnet build src/Clustral.Cli

# Native binary for current machine:
dotnet publish src/Clustral.Cli -r osx-arm64 -c Release

# Other targets:
dotnet publish src/Clustral.Cli -r linux-x64  -c Release
dotnet publish src/Clustral.Cli -r win-x64    -c Release
```

The output binary is at:
`src/Clustral.Cli/bin/Release/net10.0/<rid>/publish/clustral`

---

## Things to implement next

| # | What | Where |
|---|---|---|
| 1 | Token refresh / expiry detection before `kube login` | `OidcFlowHandler` or new `RefreshCommand` |
| 2 | `clustral configure` — interactive wizard that writes `~/.clustral/config.json` | new `Commands/ConfigureCommand.cs` |
| 3 | Credential rotation detection — warn when agent credential is near expiry | `KubeLoginCommand` + SDK |
