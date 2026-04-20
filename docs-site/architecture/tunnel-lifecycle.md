---
description: How the Clustral agent bootstraps, opens a persistent gRPC tunnel, multiplexes kubectl traffic, and reconnects after failure.
---

# Tunnel Lifecycle

The Clustral agent holds a single persistent gRPC bidirectional stream to the TunnelService. Every kubectl request is multiplexed over that stream. This page traces the tunnel from first boot through graceful shutdown.

## Overview

The tunnel is the only transport between your cluster and the Clustral control plane. It is outbound-only from the agent's perspective — the agent has no inbound ports and no inbound firewall rules are required on the cluster. mTLS and an RS256 JWT authenticate every connection; the JWT carries the cluster identity and a token version that the TunnelService checks on explicit revocation.

```mermaid
stateDiagram-v2
    [*] --> Disconnected
    Disconnected --> Bootstrapping: first boot<br/>(bootstrap token set)
    Disconnected --> Connecting: credentials present
    Bootstrapping --> Connecting: RegisterAgent OK<br/>(cert + JWT saved)
    Bootstrapping --> Disconnected: RegisterAgent fails
    Connecting --> Connected: AgentHello → TunnelHello
    Connecting --> Reconnecting: gRPC error
    Connected --> Reconnecting: stream error
    Connected --> Closed: SIGTERM
    Reconnecting --> Connecting: backoff + jitter
    Reconnecting --> Closed: PermissionDenied<br/>(credentials revoked)
    Closed --> [*]
```

## Bootstrap

The agent's first boot requires a one-time bootstrap token issued by the ControlPlane when an admin registers the cluster. The token is single-use — it's consumed atomically on `RegisterAgent` and cannot be replayed. The agent connects to the **TunnelService** (not the ControlPlane) for bootstrap.

```mermaid
sequenceDiagram
    participant A as clustral-agent
    participant TS as TunnelService :5443

    A->>A: Read AGENT_BOOTSTRAP_TOKEN, AGENT_CLUSTER_ID
    A->>TS: ClusterService.RegisterAgent<br/>(TLS, no mTLS yet)<br/>{ clusterId, bootstrapToken }
    TS->>TS: Validate token (single-use)
    TS->>TS: CA.Issue client certificate<br/>(validity = ClientCertValidityDays, 395 default)
    TS->>TS: Sign RS256 JWT for tunnel auth
    TS->>A: { clientCertPem, clientKeyPem, caCertPem, jwt, certExpiresAt, jwtExpiresAt }
    A->>A: Write /etc/clustral/tls/{client.crt,client.key,ca.crt}
    A->>A: Write /etc/clustral/agent.jwt
    A->>A: Burn bootstrap token (no longer valid)
```

The client certificate is signed by the Clustral CA, which is now hosted by TunnelService. Its validity is controlled by `CertificateAuthority:ClientCertValidityDays` (default 395 days). The JWT has a separate, shorter TTL and is renewed independently — certificate renewal and token renewal are two different RPCs (`RenewCertificate` and `RenewToken`).

{% hint style="danger" %}
If the agent loses its client certificate, its private key, or its JWT, it cannot reconnect. A new bootstrap token must be issued via `clustral clusters bootstrap <cluster>`. Back up `/etc/clustral/` or deploy the agent with persistent storage.
{% endhint %}

## Session open

Once the agent has mTLS credentials and a JWT, it opens the tunnel. The server side lives in the TunnelService, which maintains an in-memory map of `clusterId → TunnelSession` and registers each session in Redis so the ControlPlane can locate which TunnelService pod holds a given cluster's tunnel.

```mermaid
sequenceDiagram
    participant A as clustral-agent
    participant TS as TunnelService :5443
    participant REDIS as Redis

    A->>TS: grpc.NewClient(mTLS + PerRPCCredentials JWT)
    TS->>TS: TLS handshake — verify client cert against CA
    TS->>TS: AgentAuthInterceptor: verify JWT signature,<br/>issuer, audience, tokenVersion
    A->>TS: TunnelService.OpenTunnel (bidi stream)
    A->>TS: TunnelClientMessage{ hello: AgentHello }<br/>clusterId, agentVersion, kubernetesVersion
    TS->>TS: Register session for clusterId (in-memory)
    TS->>REDIS: SET tunnel session (cluster → pod address)
    TS->>TS: Evict any stale session for the same cluster
    TS->>A: TunnelServerMessage{ hello: TunnelHello }<br/>clusterId, serverTime
    Note over A,TS: Tunnel is Connected
```

Stream authentication is belt and braces:

- **mTLS** proves the agent holds a CA-signed client certificate tied to the cluster.
- **RS256 JWT** carries the `clusterId` and `tokenVersion` claims. The TunnelService's `AgentAuthInterceptor` verifies both on every RPC. If an admin revokes agent credentials, `tokenVersion` increments and the next request fails with `PermissionDenied`.

If a second agent pod connects for the same cluster (rolling deployment, split brain), the TunnelService evicts the older session and keeps the newest one. Redis is updated with the new pod assignment. `last_seen_at` on the `Cluster` record tracks stream liveness.

## Request multiplexing

The kubectl proxy handler on the ControlPlane takes each incoming HTTP request, looks up the tunnel pod in Redis, and calls `TunnelProxy.ProxyRequest` on the TunnelService via internal gRPC (:50051). The TunnelService wraps the request in an `HttpRequestFrame` and sends it through the session. The agent receives the frame, replays it as a local HTTP call to the cluster's API server, and streams the response back as one or more `HttpResponseFrame` messages tagged with the same `request_id`.

```mermaid
sequenceDiagram
    participant K as kubectl via proxy
    participant CP as ControlPlane
    participant REDIS as Redis
    participant TS as TunnelService
    participant A as clustral-agent
    participant API as k8s API Server

    K->>CP: GET /api/v1/pods
    CP->>REDIS: Lookup tunnel pod for cluster
    CP->>TS: TunnelProxy.ProxyRequest (internal gRPC :50051)
    TS->>TS: Assign request_id
    TS->>A: HttpRequestFrame{ request_id, head, body_chunk, end_of_body=true }
    A->>API: GET /api/v1/pods<br/>Authorization: Bearer <sa_token><br/>Impersonate-User, Impersonate-Group
    API->>A: 200 OK + body
    A->>TS: HttpResponseFrame{ request_id, head, body_chunk, end_of_body=true }
    TS->>CP: ProxyResponse
    CP->>K: 200 OK + body
```

A single stream carries many concurrent requests. Each side dispatches frames into a goroutine (agent) or task (TunnelService) keyed by `request_id`, and the multiplex is limited only by memory. The maximum time the ControlPlane waits for a response before returning `REQUEST_TIMEOUT` is `Proxy:TunnelTimeout` (default 2 minutes).

Per-credential rate limiting is applied before the request ever reaches the tunnel. Defaults match k8s client-go: 200-token bucket, 100 QPS refill, 50-request queue (see `Proxy:RateLimiting` in `appsettings.json`).

## Heartbeat and renewal

There is no separate heartbeat RPC. Stream-level health is the liveness signal — if the gRPC stream breaks, the agent is considered disconnected. The agent also sends periodic `ClusterService.UpdateStatus` calls (default `AGENT_HEARTBEAT_INTERVAL=30s`) to refresh `last_seen_at` and report the k8s API version discovered at startup.

The Kubernetes API version is discovered once at startup via `GET /version` on the local API server, not resent on every heartbeat. It's reported in the initial `AgentHello`.

Credential renewal runs in a separate goroutine that checks expiry every `AGENT_RENEWAL_CHECK_INTERVAL` (default 6h):

| Credential | Renewal RPC | Renew if expiry within |
|---|---|---|
| mTLS client certificate | `ClusterService.RenewCertificate` | `AGENT_CERT_RENEW_THRESHOLD` (default 720h / 30 days) |
| RS256 tunnel JWT | `ClusterService.RenewToken` | `AGENT_JWT_RENEW_THRESHOLD` (default 168h / 7 days) |

Renewal is in-band — it uses the same mTLS + JWT auth as any other RPC. `RenewToken` does not increment `tokenVersion`, so the old and new JWTs are both valid during the overlap window.

## Graceful close

On `SIGTERM`, the agent stops dispatching new frames, closes the stream cleanly, and exits. The TunnelService sees the stream close, removes the session from its in-memory map, deletes the Redis entry, and notifies the ControlPlane to mark the cluster status as `DISCONNECTED`. The next kubectl proxy request for that cluster returns `AGENT_NOT_CONNECTED` with a 503.

In-flight requests are not drained today — they fail with a stream-closed error. Graceful drain is tracked as a pending enhancement in `src/clustral-agent/CLAUDE.md`.

## Reconnection

On any stream error, the agent enters the `Reconnecting` state and backs off with exponential + jitter. On reconnect, the TunnelService registers the new session in Redis (possibly on a different pod). Defaults:

| Setting | Default |
|---|---|
| `AGENT_RECONNECT_INITIAL_DELAY` | `2s` |
| `AGENT_RECONNECT_MAX_DELAY` | `60s` |
| `AGENT_RECONNECT_BACKOFF_MULTIPLIER` | `2.0` |
| `AGENT_RECONNECT_MAX_JITTER` | `5s` |

Error-specific handling:

- **`Unauthenticated`** — agent immediately triggers JWT renewal, then reconnects. Covers the case where the JWT expired between renewal checks.
- **`PermissionDenied`** — agent stops. This status means the tunnel JWT was revoked (tokenVersion bumped) or the cluster was deregistered. Reconnecting would spin forever; the operator must re-bootstrap.
- **Any other error** — normal backoff loop.

## What can go wrong

| Event | What users see | Recovery |
|---|---|---|
| Agent pod restart | `kubectl` hangs, then returns `AGENT_NOT_CONNECTED` | Automatic reconnect; seconds. |
| Network partition | Same as above | Automatic reconnect when network returns. |
| mTLS cert expired | Agent fails at TLS handshake, loops with backoff | Re-run bootstrap with a new token: `clustral clusters bootstrap <cluster>`. |
| Tunnel JWT expired | Gateway rejects at stream open; agent renews and retries | Automatic if the expiry is caught within the renewal threshold. |
| Tunnel JWT revoked (admin action) | Agent receives `PermissionDenied` and stops | Issue a new bootstrap token and redeploy the agent. |
| Cluster deregistered | Same as above | Re-register the cluster in the Web UI or via `clustral clusters register`. |
| Redis connection failure | `kubectl` returns `AGENT_NOT_CONNECTED` (ControlPlane cannot look up tunnel pod) | Restore Redis connectivity. Agent tunnels remain open on TunnelService; proxy resumes once Redis is reachable. |
| Bootstrap token replayed | `RegisterAgent` fails with `TOKEN_ALREADY_USED` | Request a new bootstrap token. Each token is single-use. |

## See also

- [Authentication Flows](authentication-flows.md) — the kubeconfig JWT and internal JWT chain that ends at the tunnel.
- [Network Map](network-map.md) — the ports, directions, and TLS terminations involved.
- [Agent Deployment](../agent-deployment/README.md) — how to install the agent via Helm.
- [mTLS Bootstrap](../agent-deployment/mtls-bootstrap.md) — details on the bootstrap token exchange and CA trust anchor.
