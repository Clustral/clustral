# clustral-agent — Claude Code Guide

Go binary deployed into each registered Kubernetes cluster.
It has **no inbound ports** — all communication is outbound to the ControlPlane
gRPC endpoint over a persistent bidirectional stream.

This is a **Go rewrite** of the original .NET Agent (`src/Clustral.Agent/`).
The rewrite was motivated by .NET's `HttpClient` combining multi-value HTTP
headers into comma-separated values, which breaks the k8s Impersonation API.
Go's `net/http` sends each `Header.Add()` value as a separate header line
natively — no workaround needed.

---

## Component map

```
clustral-agent/
├── main.go                         ← Entry point: config, credential bootstrap, signal handling
│
├── internal/
│   ├── config/
│   │   └── config.go               ← Config struct, loaded from env vars (AGENT_*)
│   │
│   ├── credential/
│   │   └── store.go                ← Reads/writes token + expiry to disk (atomic write)
│   │
│   ├── tunnel/
│   │   └── manager.go              ← Reconnect loop + single-session logic
│   │                                 - connectAndRun: opens gRPC stream, handshakes,
│   │                                   runs dispatchFrames + heartbeat concurrently
│   │                                 - exponential backoff with jitter
│   │
│   └── proxy/
│       └── proxy.go                ← Translates HttpRequestFrame → real k8s HTTP call
│                                     - strips Authorization from tunnel request
│                                     - translates X-Clustral-Impersonate-* → k8s Impersonate-*
│                                     - SA token injection via RoundTripper
│                                     - CA cert from /var/run/secrets/.../ca.crt
│
├── gen/clustral/v1/                ← Generated protobuf + gRPC stubs (do NOT edit)
│   ├── tunnel.pb.go / _grpc.pb.go  ← TunnelService client
│   ├── auth.pb.go / _grpc.pb.go    ← AuthService client (IssueAgentCredential, Rotate)
│   └── cluster.pb.go / _grpc.pb.go ← ClusterService client (UpdateStatus heartbeat)
│
├── k8s/                            ← Kubernetes deployment manifests
│   ├── serviceaccount.yaml         ← clustral namespace + clustral-agent ServiceAccount
│   ├── clusterrole.yaml            ← impersonate verb on users/groups/serviceaccounts
│   ├── clusterrolebinding.yaml     ← binds the role to the service account
│   └── deployment.yaml             ← Pod spec with env vars from Secret
│
├── Dockerfile                      ← Multi-stage: golang:1.23-alpine → alpine:3.20 (~16MB)
├── go.mod                          ← Module: clustral-agent (local, no GitHub URL)
└── go.sum
```

---

## Startup flow

```
main()
  ├── config.Load()                   ← reads AGENT_* env vars
  ├── [Bootstrap] if AGENT_BOOTSTRAP_TOKEN set:
  │     registerAgent()
  │       gRPC ClusterService.RegisterAgent (TLS, no mTLS yet)
  │       receives: client cert + key + CA cert + JWT
  │       saves all to /etc/clustral/tls/ + agent.jwt
  │
  ├── HasMTLSCredentials()?           ← checks client.crt, client.key, ca.crt, agent.jwt
  │     false → error (needs bootstrap token)
  │
  ├── proxy.New()                     ← creates http.Client with SA token + CA cert
  ├── k8s.DiscoverVersion()           ← GET /version on k8s API (non-fatal)
  │
  ├── auth.NewJWTCredentials(jwt)     ← PerRPCCredentials with sync.RWMutex
  ├── auth.NewRenewalManager()        ← goroutine: checks cert/JWT expiry every 6h
  │
  └── tunnel.NewManager().Run(ctx)    ← reconnect loop (blocks until SIGTERM)
        └── connectAndRun()           ← one session
              ├── grpc.NewClient (mTLS: client cert + CA pool + PerRPCCredentials)
              ├── TunnelService.OpenTunnel (mTLS + JWT)
              ├── send AgentHello → recv TunnelHello
              └── errgroup.Group:
                    ├── dispatchFrames()  ← recv HttpRequestFrame → goroutine per request
                    │     proxy.Handle() → send HttpResponseFrame
                    │     PingFrame → send PongFrame
                    └── heartbeat()       ← time.Ticker → ClusterService.UpdateStatus
              Error handling:
                    Unauthenticated → immediate JWT renewal, reconnect
                    PermissionDenied → STOP (credentials revoked, no retry)
```

---

## Configuration

All settings via environment variables. No config files.

| Env var | Default | Notes |
|---|---|---|
| `AGENT_CLUSTER_ID` | *(required)* | From cluster registration |
| `AGENT_CONTROL_PLANE_URL` | *(required)* | gRPC mTLS endpoint, e.g. `https://host:5443` |
| `AGENT_CREDENTIAL_PATH` | `~/.clustral/agent.token` | In-cluster: `/etc/clustral/agent.token` |
| `AGENT_BOOTSTRAP_TOKEN` | *(required first boot)* | One-time, exchanged for mTLS cert + JWT |
| `AGENT_KUBERNETES_API_URL` | `https://kubernetes.default.svc` | In-cluster default |
| `AGENT_KUBERNETES_SKIP_TLS_VERIFY` | `false` | `true` only for local dev |
| `AGENT_HEARTBEAT_INTERVAL` | `30s` | Go duration format |
| `AGENT_CREDENTIAL_ROTATION_THRESHOLD` | `720h` | Rotate if expiry within this |
| `AGENT_RECONNECT_INITIAL_DELAY` | `2s` | |
| `AGENT_RECONNECT_MAX_DELAY` | `60s` | |
| `AGENT_RECONNECT_BACKOFF_MULTIPLIER` | `2.0` | |
| `AGENT_RECONNECT_MAX_JITTER` | `5s` | |
| `AGENT_VERSION` | `0.1.0` | Sent in AgentHello |
| `AGENT_CERT_RENEW_THRESHOLD` | `720h` | Renew cert if expiry within this |
| `AGENT_JWT_RENEW_THRESHOLD` | `168h` | Renew JWT if expiry within this |
| `AGENT_RENEWAL_CHECK_INTERVAL` | `6h` | How often to check cert/JWT expiry |

---

## Impersonation (the reason for the Go rewrite)

The ControlPlane injects `X-Clustral-Impersonate-User` and
`X-Clustral-Impersonate-Group` headers into each `HttpRequestFrame`.
The agent translates these to k8s `Impersonate-User` / `Impersonate-Group`
headers.

```go
// proxy.go — this is why Go was chosen over .NET
if lower == "x-clustral-impersonate-group" {
    req.Header.Add("Impersonate-Group", h.Value)  // separate header per group
}
```

Go's `Header.Add()` stores each value independently. When serialized to the
wire, Go sends them as **separate header lines** — which is what the k8s API
requires. .NET's `HttpClient` combines them into one comma-separated line,
which k8s rejects.

The `ClusterRole` only needs the `impersonate` verb — k8s RBAC enforces
permissions per the impersonated user/groups.

This behaviour is regression-tested end-to-end by `RoleBasedAccessTests` in
`src/Clustral.E2E.Tests/`. That suite runs the **real** agent binary (built
from this Dockerfile) against a real K3s cluster, so any change that breaks
multi-value impersonation header forwarding fails RBAC against k3s.

---

## In-cluster auth

The agent authenticates to the k8s API using the pod's ServiceAccount token:

```go
// proxy.go — saTokenRoundTripper
func (rt *saTokenRoundTripper) RoundTrip(req *http.Request) (*http.Response, error) {
    data, _ := os.ReadFile(rt.tokenPath)  // re-read on each request (kubelet rotates hourly)
    req.Header.Set("Authorization", "Bearer "+strings.TrimSpace(string(data)))
    return rt.inner.RoundTrip(req)
}
```

The CA cert is loaded from `/var/run/secrets/kubernetes.io/serviceaccount/ca.crt`
into `tls.Config.RootCAs` for TLS verification against the k8s API server.

---

## Local dev

```bash
# Set required env vars
export AGENT_CLUSTER_ID="<from-registration>"
export AGENT_CONTROL_PLANE_URL="https://localhost:5443"
export AGENT_BOOTSTRAP_TOKEN="<from-registration>"
export AGENT_KUBERNETES_SKIP_TLS_VERIFY=true  # for Docker Desktop k8s

# Run
cd src/clustral-agent
go run .
```

## Build the Docker image

```bash
cd src/clustral-agent
docker build -t clustral-agent:dev .
# Result: ~16MB alpine image with static Go binary
```

## Regenerate proto stubs

```bash
cd src/clustral-agent
export PATH="$PATH:$(go env GOPATH)/bin"
protoc \
  --go_out=gen --go_opt=paths=source_relative \
  --go_opt=Mtunnel.proto=clustral-agent/gen/clustral/v1 \
  --go_opt=Mauth.proto=clustral-agent/gen/clustral/v1 \
  --go_opt=Mcluster.proto=clustral-agent/gen/clustral/v1 \
  --go-grpc_out=gen --go-grpc_opt=paths=source_relative \
  --go-grpc_opt=Mtunnel.proto=clustral-agent/gen/clustral/v1 \
  --go-grpc_opt=Mauth.proto=clustral-agent/gen/clustral/v1 \
  --go-grpc_opt=Mcluster.proto=clustral-agent/gen/clustral/v1 \
  -I../../packages/proto \
  ../../packages/proto/tunnel.proto \
  ../../packages/proto/auth.proto \
  ../../packages/proto/cluster.proto
```

## Apply RBAC manifests

```bash
kubectl apply -f src/clustral-agent/k8s/
```

---

## Key differences from the .NET Agent

| Aspect | .NET Agent (`Clustral.Agent`) | Go Agent (`clustral-agent`) |
|---|---|---|
| Language | C# / .NET 10 | Go 1.23 |
| Binary size | ~80MB (runtime image) | ~16MB (static binary) |
| Impersonation | Custom `ImpersonationHandler` (raw HTTP) | Native `Header.Add()` |
| Config | `appsettings.json` + `Agent__*` env vars | `AGENT_*` env vars only |
| SA token | `ServiceAccountTokenHandler` (DelegatingHandler) | `saTokenRoundTripper` (RoundTripper) |
| TLS | `SslStream` + custom CA validation | `tls.Config.RootCAs` |
| Concurrency | `SemaphoreSlim` + `Task.WhenAny` | `errgroup.Group` + goroutines |
| gRPC | `Grpc.Net.Client` | `google.golang.org/grpc` |
| Module path | N/A | `clustral-agent` (local, no GitHub URL) |

---

## Things to implement next

| # | What | Where |
|---|---|---|
| 1 | Chunked response streaming (multiple `HttpResponseFrame` per request) | `proxy.Handle` |
| 2 | `CancelFrame` handling — cancel in-flight goroutine via context | `tunnel.dispatchFrames` |
| ~~3~~ | ~~k8s version discovery~~ | **Done** — `internal/k8s/version.go` |
| 4 | mTLS between Agent and ControlPlane | `tunnel.connectAndRun` gRPC dial options |
| 5 | Graceful shutdown — drain in-flight requests before closing stream | `tunnel.Run` |
| 6 | Prometheus metrics endpoint | new `internal/metrics/` package |
