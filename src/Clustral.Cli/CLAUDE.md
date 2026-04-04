# Clustral.Cli — Claude Code Guide

NativeAOT console application that gives developers a `clustral` binary for
authenticating with Keycloak and writing kubeconfig credentials.

---

## Command tree

```
clustral
├── login                         ← OIDC PKCE flow; writes JWT to ~/.clustral/token
└── kube
    └── login <cluster-id>        ← REST call to ControlPlane; writes ~/.kube/config entry
```

---

## File map

```
Clustral.Cli/
├── Program.cs                    ← Builds the root command and invokes System.CommandLine
│
├── Commands/
│   ├── LoginCommand.cs           ← clustral login
│   └── KubeLoginCommand.cs       ← clustral kube / clustral kube login
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
| 1 | `clustral logout` — clears `~/.clustral/token` | new `Commands/LogoutCommand.cs` |
| 2 | `clustral kube list` — lists available cluster IDs from ControlPlane | new subcommand under `kube` |
| 3 | `clustral kube remove <context-name>` — calls `KubeconfigWriter.RemoveClusterEntry` | new subcommand under `kube` |
| 4 | `clustral configure` — interactive wizard that writes `~/.clustral/config.json` | new command |
| 5 | Token refresh / expiry detection before `kube login` | `LoginCommand` or new `RefreshCommand` |
