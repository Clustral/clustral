# Architecture

Clustral is composed of four main services: the ControlPlane (ASP.NET Core), the API Gateway (YARP), the Web UI (Next.js), and per-cluster Agents (Go). They communicate via REST, gRPC tunnels, and an event bus (RabbitMQ).

## Pages in this section

- [Authentication Flows](authentication-flows.md) -- OIDC PKCE, kubeconfig credentials, internal JWTs
- [Tunnel Lifecycle](tunnel-lifecycle.md) -- Agent bootstrap, mTLS handshake, bidirectional streaming
- [Network Map](network-map.md) -- Port reference, nginx routing, firewall requirements
