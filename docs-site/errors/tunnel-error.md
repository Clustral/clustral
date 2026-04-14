---
code: TUNNEL_ERROR
http_status: 500
kind: Internal
category: Auth & Proxy
emitted_by:
  - "ResultErrors.TunnelError"
---

# TUNNEL_ERROR

> **HTTP 500** | `Internal` | Category: Auth & Proxy

<!-- AUTO-GEN-START -->
**Default message:** An internal error occurred while forwarding the request through the agent tunnel: <placeholder>. This is typically transient — retry the command.

**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/tunnel-error`](https://docs.clustral.kube.it.com/errors/tunnel-error)
<!-- AUTO-GEN-END -->

## What this means

An internal error occurred while forwarding the request through the agent's gRPC tunnel. The tunnel stream may have been interrupted or the message framing was corrupted.

## Why it happens

- Transient network interruption between the agent and ControlPlane.
- The agent process restarted mid-request.
- A bug in the tunnel framing protocol.

## How to fix

1. Retry the command -- tunnel errors are usually transient.
2. Check `clustral clusters list` to verify the agent is still connected.
3. If the error persists, check agent logs: `kubectl -n clustral logs deploy/clustral-agent`.

## Example response

```
HTTP/1.1 500
Content-Type: text/plain
X-Clustral-Error-Code: TUNNEL_ERROR
X-Correlation-Id: <uuid>
Link: <https://docs.clustral.kube.it.com/errors/tunnel-error>; rel="help"

An internal error occurred while forwarding the request through the agent tunnel: <placeholder>. This is typically transient — retry the command.
```

## See also

- [AGENT_NOT_CONNECTED](agent-not-connected.md) -- agent completely disconnected
- [TUNNEL_TIMEOUT](tunnel-timeout.md) -- agent connected but too slow
