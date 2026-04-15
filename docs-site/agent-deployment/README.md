---
description: Deploy the Clustral agent to your Kubernetes clusters — Helm-chart values, bootstrap token lifecycle, and mTLS credential renewal.
---

# Agent Deployment

The Clustral agent is a Go binary deployed into target Kubernetes clusters. It establishes a persistent gRPC tunnel back to the ControlPlane, enabling kubectl traffic without inbound firewall rules.

## Pages in this section

- [Helm Chart](helm-chart.md) -- Chart values, installation, and configuration
- [mTLS Bootstrap](mtls-bootstrap.md) -- How the agent obtains its client certificate
