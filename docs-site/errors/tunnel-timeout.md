---
code: TUNNEL_TIMEOUT
http_status: 500
kind: Internal
category: Auth & Proxy
emitted_by:
  - "ResultErrors.TunnelTimeout"
---

# TUNNEL_TIMEOUT

> **HTTP 500** | `Internal` | Category: Auth & Proxy

<!-- AUTO-GEN-START -->
**Default message:** The Clustral agent did not respond within 00:02:00. The Kubernetes API server may be slow or the agent's network connectivity may be degraded. Try again shortly; if the problem persists, check the agent pod logs.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/tunnel-timeout`](https://docs.clustral.kube.it.com/errors/tunnel-timeout)
<!-- AUTO-GEN-END -->

## What this means

The ControlPlane forwarded the request through the agent tunnel, but the agent did not respond within the configured timeout (default 2 minutes).

## Why it happens

- The Kubernetes API server on the target cluster is overloaded or unresponsive.
- The agent's network path to the Kubernetes API is degraded.
- The request is unusually large (e.g., streaming a large log).

## How to fix

1. Retry the command -- this is often transient.
2. Check the agent pod logs for errors: `kubectl -n clustral logs deploy/clustral-agent`.
3. Verify the Kubernetes API server is healthy on the target cluster.
4. If the default timeout is too short, ask your administrator to increase `Proxy:TunnelTimeout`.

## Example response

```
HTTP/1.1 500
Content-Type: text/plain
X-Clustral-Error-Code: TUNNEL_TIMEOUT
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/tunnel-timeout>; rel="help"

The Clustral agent did not respond within 00:02:00. The Kubernetes API server may be slow or the agent's network connectivity may be degraded. Try again shortly; if the problem persists, check the agent pod logs.
```

## See also

- [AGENT_NOT_CONNECTED](agent-not-connected.md) -- agent not connected at all
- [AGENT_ERROR](agent-error.md) -- agent connected but reported an error
