# Clustral.Agent — Claude Code Guide

.NET Worker Service deployed into each registered Kubernetes cluster.
It has **no inbound ports** — all communication is outbound to the ControlPlane
gRPC endpoint over a persistent bidirectional stream.

---

## Component map

```
Clustral.Agent/
├── Program.cs                  ← Host setup: options, HttpClient, singletons, hosted service
├── AgentOptions.cs             ← Typed config bound from Agent: section
├── AgentCredentialStore.cs     ← Reads/writes {CredentialPath} and {CredentialPath}.expiry
│
├── Worker/
│   └── AgentWorker.cs          ← BackgroundService entry point
│                                 - ensures credential on boot (issue / rotate)
│                                 - delegates to TunnelManager.RunAsync
│
├── Tunnel/
│   └── TunnelManager.cs        ← Reconnect loop + single-session logic
│                                 - ConnectAndRunAsync: opens stream, handshakes,
│                                   runs DispatchFramesAsync + HeartbeatAsync concurrently
│                                 - exponential backoff with jitter
│
├── Proxy/
│   └── KubectlProxy.cs         ← Translates HttpRequestFrame → real k8s HTTP call
│                                 - strips Authorization from tunnel request
│                                 - adds Service Account token for in-cluster auth
│                                 - CreateKubernetesHttpClient factory (in-cluster vs dev)
│
└── k8s/
    ├── serviceaccount.yaml     ← clustral namespace + clustral-agent ServiceAccount
    ├── clusterrole.yaml        ← broad read/write permissions (see security note below)
    └── clusterrolebinding.yaml ← binds the role to the service account
```

---

## Startup flow

```
AgentWorker.ExecuteAsync
  └── EnsureCredentialAsync
        ├── No credential file?  → IssueCredentialAsync
        │     calls AuthService.IssueAgentCredential with bootstrap token
        │     writes token + expiry to CredentialPath
        └── Credential expires soon? → RotateCredentialAsync
              calls AuthService.RotateAgentCredential
              overwrites CredentialPath

  └── TunnelManager.RunAsync  (reconnect loop)
        └── ConnectAndRunAsync  (one session)
              ├── creates GrpcChannel with agent token
              ├── TunnelService.OpenTunnel bidirectional stream
              ├── sends AgentHello
              ├── waits for TunnelHello
              └── runs concurrently:
                    DispatchFramesAsync   ← receive HttpRequestFrame → ProxyAndReplyAsync
                    HeartbeatAsync        ← PeriodicTimer → ClusterService.UpdateStatus
```

---

## Configuration

All settings live under the `Agent:` key in `appsettings.json`.
In k8s, override with environment variables (`Agent__BootstrapToken`, etc.)
or a ConfigMap projected as env vars.

| Key | Default | Notes |
|---|---|---|
| `ClusterId` | *(required)* | Set at deploy time from Helm values |
| `ControlPlaneUrl` | *(required)* | HTTPS for prod, HTTP for kind dev |
| `CredentialPath` | `/etc/clustral/agent.token` | Mount a Secret volume here in k8s |
| `BootstrapToken` | *(required first boot)* | Supply via Secret env var |
| `AgentPublicKeyPem` | *(required first boot)* | Must match registration key |
| `KubernetesApiUrl` | `https://kubernetes.default.svc` | |
| `KubernetesSkipTlsVerify` | `false` | `true` only for kind |
| `HeartbeatInterval` | `00:00:30` | |
| `CredentialRotationThreshold` | `30.00:00:00` | Rotate if expiry < 30 days away |
| `Reconnect:InitialDelay` | `00:00:02` | |
| `Reconnect:MaxDelay` | `00:01:00` | |
| `Reconnect:BackoffMultiplier` | `2.0` | |
| `Reconnect:MaxJitter` | `00:00:05` | |

---

## RBAC security note

`k8s/clusterrole.yaml` grants broad access because the Agent acts as a
transparent proxy for all ControlPlane-authenticated users.

**For production, implement Kubernetes user impersonation** so the k8s API
server enforces per-user RBAC itself:

```http
Impersonate-User: alice@example.com
Impersonate-Group: system:authenticated
```

`KubectlProxy` would add these headers (populated from the identity validated
by `ValidateKubeconfigCredential`) instead of relying solely on the agent's
ServiceAccount permissions.  With impersonation the agent's ServiceAccount
only needs `impersonate` verb, which is a much smaller blast radius.

---

## Local dev

```bash
# Start the ControlPlane (see its CLAUDE.md)
# Then run the Agent against the local kind cluster:
dotnet run --project src/Clustral.Agent \
  -- \
  --Agent:ClusterId=<your-cluster-id> \
  --Agent:ControlPlaneUrl=http://localhost:5001 \
  --Agent:BootstrapToken=<token-from-register> \
  --Agent:AgentPublicKeyPem="$(cat ~/.clustral/agent.pub)"
```

## Build the Docker image

```bash
docker build \
  -f src/Clustral.Agent/Dockerfile \
  -t clustral-agent:dev \
  .
```

## Apply RBAC manifests

```bash
kubectl apply -f src/Clustral.Agent/k8s/
```

---

## Things to implement next

| # | What | Where |
|---|---|---|
| 1 | Chunked response streaming (multiple `HttpResponseFrame` per request) | `KubectlProxy.ProxyAsync` |
| 2 | `CancelFrame` handling — cancel the `ProxyAndReplyAsync` task for the given `request_id` | `TunnelManager.DispatchFramesAsync` |
| 3 | k8s version discovery (`GET /version`) | `TunnelManager.DiscoverKubernetesVersionAsync` |
| 4 | User impersonation headers | `KubectlProxy.BuildRequest` |
| 5 | mTLS between Agent and ControlPlane | `TunnelManager.CreateChannel` |
