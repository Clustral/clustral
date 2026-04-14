---
code: AGENT_NOT_CONNECTED
http_status: 500
kind: Internal
category: Auth & Proxy
emitted_by:
  - "ResultErrors.AgentNotConnected"
---

# AGENT_NOT_CONNECTED

> **HTTP 500** | `Internal` | Category: Auth & Proxy

<!-- AUTO-GEN-START -->
**Default message:** The Clustral agent for cluster 00000000-0000-0000-0000-000000000000 is not currently connected to the ControlPlane. The cluster may be offline or the agent deployment may be unhealthy. Check 'clustral clusters list' for the cluster's status, and verify the agent pod is running in-cluster.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/agent-not-connected`](https://docs.clustral.kube.it.com/errors/agent-not-connected)
<!-- AUTO-GEN-END -->

## What this means

The ControlPlane has no active gRPC tunnel session for the target cluster. Without a connected agent, kubectl requests cannot be forwarded to the Kubernetes API.

## Why it happens

- The agent pod is not running or has crashed.
- The agent cannot reach the ControlPlane gRPC endpoint (network/firewall issue).
- The agent's credentials have been revoked or expired and it failed to renew.
- The cluster was recently registered and the agent hasn't connected yet.

## How to fix

1. Check the cluster status: `clustral clusters list`.
2. Verify the agent pod is running: `kubectl -n clustral get pods`.
3. Check agent logs: `kubectl -n clustral logs deploy/clustral-agent`.
4. If the agent is running but not connecting, verify the `AGENT_CONTROL_PLANE_URL` env var points at the correct gRPC endpoint.

## Example response

```
HTTP/1.1 500
Content-Type: text/plain
X-Clustral-Error-Code: AGENT_NOT_CONNECTED
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/agent-not-connected>; rel="help"

The Clustral agent for cluster 00000000-0000-0000-0000-000000000000 is not currently connected to the ControlPlane. The cluster may be offline or the agent deployment may be unhealthy. Check 'clustral clusters list' for the cluster's status, and verify the agent pod is running in-cluster.
```

## See also

- [TUNNEL_TIMEOUT](tunnel-timeout.md) -- agent connected but Kubernetes API slow
- [TUNNEL_ERROR](tunnel-error.md) -- agent connected but tunnel communication failed
