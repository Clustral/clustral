---
code: AGENT_ERROR
http_status: 500
kind: Internal
category: Auth & Proxy
emitted_by:
  - "ResultErrors.AgentError"
---

# AGENT_ERROR

> **HTTP 500** | `Internal` | Category: Auth & Proxy

<!-- AUTO-GEN-START -->
**Default message:** The Clustral agent reported an error while proxying to the Kubernetes API (<placeholder>): <placeholder>. This usually means the agent cannot reach the Kubernetes API server — check the agent pod's network access.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/agent-error`](https://docs.clustral.kube.it.com/errors/agent-error)
<!-- AUTO-GEN-END -->

## What this means

The agent received the request and attempted to forward it to the Kubernetes API, but the k8s API call itself failed. The agent reported the error back through the tunnel.

## Why it happens

- The agent cannot reach the Kubernetes API server (DNS, network, or TLS issue).
- The agent's ServiceAccount token is expired or the SA was deleted.
- The Kubernetes API server returned a non-HTTP error (e.g., TLS handshake failure).

## How to fix

1. Check the agent pod logs for the detailed error: `kubectl -n clustral logs deploy/clustral-agent`.
2. Verify the agent SA exists: `kubectl -n clustral get sa clustral-agent`.
3. Verify the agent can reach the k8s API: `kubectl -n clustral exec deploy/clustral-agent -- wget -qO- https://kubernetes.default.svc/healthz`.

## Example response

```
HTTP/1.1 500
Content-Type: text/plain
X-Clustral-Error-Code: AGENT_ERROR
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/agent-error>; rel="help"

The Clustral agent reported an error while proxying to the Kubernetes API (<placeholder>): <placeholder>. This usually means the agent cannot reach the Kubernetes API server — check the agent pod's network access.
```

## See also

- [AGENT_NOT_CONNECTED](agent-not-connected.md) -- agent not connected
- [TUNNEL_TIMEOUT](tunnel-timeout.md) -- timeout waiting for agent
