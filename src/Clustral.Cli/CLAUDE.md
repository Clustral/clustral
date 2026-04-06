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
│   ├── login <cluster-id>          ← Issue kubeconfig credential
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
│   └── AccessCommand.cs          ← clustral access (request, list, approve, deny, revoke)
│
├── Ui/
│   └── CliErrors.cs              ← Card-style error display via Spectre.Console panels
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

## kube login flow (`clustral kube login <cluster-id>`)

```
KubeLoginCommand.HandleAsync
  1. TokenCache.ReadAsync                               (reads ~/.clustral/token)
  2. POST /api/v1/credentials/kubeconfig                (IssueCredentialRequest → IssueCredentialResponse)
  3. KubeconfigWriter.WriteClusterEntry                 (upserts ~/.kube/config)
     ContextName = clustral-<cluster-id>  (override with --context-name)
     ServerUrl   = {ControlPlaneUrl}/proxy/<cluster-id>
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

## Error Display

The CLI uses `Ui/CliErrors.cs` for consistent, card-style error output via
Spectre.Console panels. Four methods are available:

| Method | Purpose |
|---|---|
| `WriteHttpError` | Formats HTTP error responses (status code + body) into a bordered panel |
| `WriteConnectionError` | Displays connection failures (e.g. ControlPlane unreachable) with actionable hints |
| `WriteError` | General-purpose error panel for unexpected errors |
| `WriteNotConfigured` | Shown when `~/.clustral/config.json` is missing or incomplete; guides the user to run `clustral login` |

---

## Spectre.Console

The CLI uses [Spectre.Console](https://spectreconsole.net/) for rich terminal
output including tables, panels, and markup formatting. The previous custom
`Ansi.cs` helper was removed in favour of Spectre.Console's built-in
capabilities.

---

## Testing

Every new CLI command or feature must include tests in `src/Clustral.Cli.Tests/`:

- **Command tree tests**: verify the command is registered with correct options/arguments.
- **Render tests**: use `TestConsole` from `Spectre.Console.Testing` to capture and assert on visual output via `ITestOutputHelper`.
- **Wire type tests**: verify JSON serialization round-trips for any new DTO types in `CliJsonContext`.
- **Error display tests**: test `CliErrors.*` card rendering for new error scenarios.

Frontend (Web UI) tests are not yet required but will be added later.

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
