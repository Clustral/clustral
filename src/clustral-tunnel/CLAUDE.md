# clustral-tunnel -- Claude Code Guide

Go service that manages gRPC agent tunnels. This service takes over tunnel
management from the .NET ControlPlane, allowing the ControlPlane to remain
stateless while tunnel sessions are pinned to specific pods.

---

## Architecture

The tunnel service runs three listeners:

| Port | Protocol | Auth | Purpose |
|------|----------|------|---------|
| `:5443` (TUNNEL_AGENT_PORT) | gRPC + mTLS | Client cert + RS256 JWT | Agent tunnel streams and cluster lifecycle RPCs |
| `:50051` (TUNNEL_INTERNAL_PORT) | gRPC | None (pod-local) | TunnelProxy -- ControlPlane forwards kubectl requests here |
| `:8081` (TUNNEL_HEALTH_PORT) | HTTP | None | `/healthz` liveness probe |

### Request flow

```
Agent --> mTLS+JWT --> :5443 TunnelService.OpenTunnel (persistent bidi stream)
                            ClusterService.* (register, heartbeat, renew)

ControlPlane --> Redis lookup (which pod?) --> :50051 TunnelProxy.ProxyRequest
                                                      |
                                                      v
                                               Local session.ProxyAsync()
                                                      |
                                                      v
                                               Agent stream --> k8s API
```

---

## Component map

```
clustral-tunnel/
  main.go                         -- Entry point: config, servers, signal handling
  internal/
    config/config.go              -- Env var configuration
    auth/
      jwt_validator.go            -- RS256 JWT parsing + validation
      interceptor.go              -- gRPC unary+stream interceptor (mTLS+JWT)
    tunnel/
      session.go                  -- Single agent tunnel session (proxy + pending requests)
      manager.go                  -- Session registry (local map + Redis)
      service.go                  -- TunnelService gRPC server (OpenTunnel handler)
    cluster/
      ca.go                       -- Certificate authority (issue agent client certs)
      jwt_signer.go               -- RS256 JWT issuer for agent tokens
      service.go                  -- ClusterService gRPC server (Register, RegisterAgent, etc.)
    proxy/
      service.go                  -- TunnelProxy gRPC server (internal, no auth)
    redis/
      client.go                   -- Redis session registry (register/lookup/refresh/unregister)
  gen/
    clustral/v1/                  -- Generated protobuf stubs (tunnel.proto, cluster.proto)
    tunnelproxy/v1/               -- Generated protobuf stubs (tunnel_proxy.proto)
```

---

## Environment variables

| Env var | Default | Notes |
|---|---|---|
| `TUNNEL_AGENT_PORT` | `5443` | Agent-facing gRPC port (mTLS) |
| `TUNNEL_INTERNAL_PORT` | `50051` | Internal gRPC port (no auth) |
| `TUNNEL_HEALTH_PORT` | `8081` | Health HTTP port |
| `TUNNEL_MONGO_URI` | `mongodb://localhost:27017` | MongoDB connection |
| `TUNNEL_MONGO_DATABASE` | `clustral` | MongoDB database name |
| `TUNNEL_REDIS_URL` | `localhost:6379` | Redis for session registry |
| `TUNNEL_CA_CERT_PATH` | *(required)* | Path to CA certificate PEM |
| `TUNNEL_CA_KEY_PATH` | *(required)* | Path to CA private key PEM (RSA) |
| `TUNNEL_SESSION_TTL` | `5m` | Redis session key TTL |
| `TUNNEL_POD_NAME` | `tunnel-0` | Pod identity for Redis ownership |
| `TUNNEL_JWT_VALIDITY_DAYS` | `90` | Agent JWT validity period |
| `TUNNEL_CERT_VALIDITY_DAYS` | `365` | Agent certificate validity period |

---

## Local dev

```bash
# Prerequisites: Redis + MongoDB running (via docker-compose)
export TUNNEL_CA_CERT_PATH=/path/to/ca.crt
export TUNNEL_CA_KEY_PATH=/path/to/ca.key
cd src/clustral-tunnel
go run .
```

---

## Proto contracts

The service implements three proto contracts from `packages/proto/`:

- **TunnelService** (`tunnel.proto`) -- `OpenTunnel` bidi stream for agent tunnels
- **ClusterService** (`cluster.proto`) -- Agent lifecycle: Register, RegisterAgent, RenewCertificate, RenewToken, UpdateStatus, List, Get, Deregister
- **TunnelProxy** (`tunnel_proxy.proto`) -- Internal: `ProxyRequest` for kubectl forwarding

### Regenerate stubs

```bash
cd src/clustral-tunnel
export PATH="$PATH:$(go env GOPATH)/bin"

# TunnelService + ClusterService
protoc \
  --go_out=gen/clustral/v1 --go_opt=paths=source_relative \
  --go_opt=Mtunnel.proto=clustral-tunnel/gen/clustral/v1 \
  --go_opt=Mcluster.proto=clustral-tunnel/gen/clustral/v1 \
  --go-grpc_out=gen/clustral/v1 --go-grpc_opt=paths=source_relative \
  --go-grpc_opt=Mtunnel.proto=clustral-tunnel/gen/clustral/v1 \
  --go-grpc_opt=Mcluster.proto=clustral-tunnel/gen/clustral/v1 \
  -I../../packages/proto \
  ../../packages/proto/tunnel.proto \
  ../../packages/proto/cluster.proto

# TunnelProxy (internal)
protoc \
  --go_out=gen/tunnelproxy/v1 --go_opt=paths=source_relative \
  --go_opt=Mtunnel_proxy.proto=clustral-tunnel/gen/tunnelproxy/v1 \
  --go_opt=Mtunnel.proto=clustral-tunnel/gen/clustral/v1 \
  --go_opt=Mcluster.proto=clustral-tunnel/gen/clustral/v1 \
  --go-grpc_out=gen/tunnelproxy/v1 --go-grpc_opt=paths=source_relative \
  --go-grpc_opt=Mtunnel_proxy.proto=clustral-tunnel/gen/tunnelproxy/v1 \
  --go-grpc_opt=Mtunnel.proto=clustral-tunnel/gen/clustral/v1 \
  --go-grpc_opt=Mcluster.proto=clustral-tunnel/gen/clustral/v1 \
  -I../../packages/proto \
  ../../packages/proto/tunnel_proxy.proto
```

---

## Testing

```bash
cd src/clustral-tunnel
go test -race ./...
```

Tests use `miniredis` for Redis (no real Redis needed) and `testify` for assertions.
MongoDB-dependent tests (ClusterService CRUD) require Testcontainers or a live instance.

---

## Relationship to other components

- **clustral-agent** -- connects to this service via mTLS on `:5443`. Opens `OpenTunnel` stream, calls `UpdateStatus` heartbeat, `RenewCertificate`/`RenewToken` for credential rotation.
- **ControlPlane (.NET)** -- queries Redis for tunnel pod ownership, then calls `TunnelProxy.ProxyRequest` on `:50051` to forward kubectl traffic. ControlPlane remains stateless.
- **MongoDB** -- shared `clustral` database. This service reads/writes the `clusters` collection directly using the same schema as the .NET ControlPlane.
