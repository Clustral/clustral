---
description: Install and configure the Clustral agent in a Kubernetes cluster with the Helm chart.
---

# Helm Chart

The Clustral agent is a single Go binary deployed as a `Deployment` in the target cluster. It opens one outbound gRPC mTLS connection to the control plane on port `5443` and receives kubectl traffic over that tunnel.

## Overview

Nothing else runs inside the cluster. No inbound ports. No sidecars. No operator. The installed resources are a `ServiceAccount`, a `ClusterRole` + `ClusterRoleBinding` that grant the `impersonate` verb, a `Deployment`, and a `Secret` holding the bootstrap token and control-plane URL.

## Prerequisites

- Helm 3.12 or newer.
- `kubectl` pointed at the target cluster with permission to create cluster-scoped resources.
- A one-time bootstrap token from `clustral clusters register <name>` (admin only).
- Egress from the cluster to the control plane on TCP `5443`.

## Install

Register the cluster against the control plane — this runs on a machine where you are already signed in with `clustral login`:

```bash
clustral clusters register my-cluster
# → clusterId:       a3f7c1e0-2b4d-4a3f-8c9e-1b2d3f4a5c6d
#   bootstrapToken:  bst_ey...
```

Add the Helm repository (once per machine) and install the agent:

```bash
helm repo add clustral https://clustral.github.io/clustral
helm repo update
helm install clustral-agent clustral/clustral-agent \
  --namespace clustral-system --create-namespace \
  --set controlPlaneUrl=clustral.example.com:5443 \
  --set clusterId=a3f7c1e0-2b4d-4a3f-8c9e-1b2d3f4a5c6d \
  --set bootstrapToken=bst_ey...
```

{% hint style="warning" %}
The bootstrap token is single-use. The first successful `RegisterAgent` consumes it and replay attempts fail with `TOKEN_ALREADY_USED`. Don't put the token in a values file that gets checked in — pass it on the command line or pipe it from a secret store.
{% endhint %}

## Values reference

| Value | Required | Default | Description |
|---|---|---|---|
| `controlPlaneUrl` | yes | — | Control-plane `host:port` for the mTLS tunnel. Example `clustral.example.com:5443`. Maps to `AGENT_CONTROL_PLANE_URL`. |
| `clusterId` | yes | — | GUID returned by `clustral clusters register`. Maps to `AGENT_CLUSTER_ID`. |
| `bootstrapToken` | yes, first install | — | Single-use token from `clustral clusters register`. Not required after the agent has written mTLS credentials to its volume. |
| `image.repository` | no | `ghcr.io/clustral/clustral-agent` | |
| `image.tag` | no | `main` | Pin to a specific version for production (e.g. `v0.4.0`). |
| `image.pullPolicy` | no | `Always` | |
| `replicaCount` | no | `1` | `>1` provides failover, but each replica maintains its own tunnel session and `TunnelSessionManager` evicts all but the newest. Keep at `1` unless you need fast failover. |
| `resources.requests.cpu` | no | `50m` | |
| `resources.requests.memory` | no | `32Mi` | |
| `resources.limits.cpu` | no | `200m` | |
| `resources.limits.memory` | no | `64Mi` | |
| `serviceAccount.create` | no | `true` | |
| `serviceAccount.name` | no | `clustral-agent` | |
| `rbac.create` | no | `true` | Creates the `ClusterRole` + `ClusterRoleBinding` granting `impersonate`. |
| `namespace` | no | `clustral-system` | Target namespace. Created by `--create-namespace`. |
| `logLevel` | no | `info` | `debug` / `info` / `warn` / `error`. |
| `env.agentHeartbeatInterval` | no | `30s` | Maps to `AGENT_HEARTBEAT_INTERVAL`. |
| `env.agentCertRenewThreshold` | no | `720h` | Maps to `AGENT_CERT_RENEW_THRESHOLD`. |
| `env.agentJwtRenewThreshold` | no | `168h` | Maps to `AGENT_JWT_RENEW_THRESHOLD`. |
| `env.agentKubernetesSkipTlsVerify` | no | `false` | Dev-only. Maps to `AGENT_KUBERNETES_SKIP_TLS_VERIFY`. |
| `caCertConfigMap` | no | — | Override the embedded control-plane CA for custom-PKI deployments. |
| `nodeSelector` | no | `{}` | |
| `tolerations` | no | `[]` | |
| `affinity` | no | `{}` | |

Run `helm show values clustral/clustral-agent` to see the full values file. See `src/clustral-agent/CLAUDE.md` for the full `AGENT_*` environment-variable reference the binary consumes.

## RBAC

The chart creates a `ClusterRole` with `impersonate` on `users`, `groups`, `serviceaccounts` across all API groups (wildcard to avoid conflicts with CRDs like Rancher's `management.cattle.io/users`). It also grants `impersonate` on `authentication.k8s.io/*`, plus read on `nodes`, plus `create` on `selfsubjectaccessreviews` / `selfsubjectrulesreviews` so `kubectl auth can-i` works through the proxy.

The exact manifest:

```yaml
rules:
  - apiGroups: ["*"]
    resources: ["users", "groups", "serviceaccounts"]
    verbs: ["impersonate"]
  - apiGroups: ["authentication.k8s.io"]
    resources: ["*"]
    verbs: ["impersonate"]
  - apiGroups: [""]
    resources: ["nodes"]
    verbs: ["get", "list"]
  - apiGroups: ["authorization.k8s.io"]
    resources: ["selfsubjectaccessreviews", "selfsubjectrulesreviews"]
    verbs: ["create"]
```

The agent never performs a read or write as itself — every request is impersonated to the calling user. The cluster's own RBAC is what ends up enforced against real identities.

## Namespace placement

The default namespace is `clustral-system`. Nothing about the design requires a specific namespace; use whichever matches your tooling conventions. Pod Security Standards of `baseline` or stricter are fine — the agent is a non-root, non-privileged container with no `hostPath` mounts.

## Verifying the install

```bash
kubectl -n clustral-system logs deploy/clustral-agent --tail=50
# Look for:
#   "Registered agent, wrote TLS credentials"
#   "Tunnel session opened clusterId=<uuid>"
```

From an admin machine:

```bash
clustral clusters list
# Expect: my-cluster  Connected  <agent-version>  <k8s-version>
```

End-to-end check:

```bash
clustral kube login my-cluster
kubectl get pods -A
```

## Upgrading

```bash
helm upgrade clustral-agent clustral/clustral-agent \
  --namespace clustral-system \
  --reuse-values \
  --set image.tag=v0.4.0
```

Follow the **control plane first, agents second** order documented in [Operator Guide — Upgrade](../operator-guide/upgrade.md). The gRPC contract is additively versioned: a newer control plane always speaks to older agents. The reverse is not guaranteed.

## Uninstall

```bash
helm uninstall clustral-agent -n clustral-system
clustral clusters deregister my-cluster   # on the control-plane side
```

Deregistering invalidates any outstanding tunnel JWT for the cluster so a forgotten pod cannot reconnect.

## Customizing

### Air-gapped registries

Mirror `ghcr.io/clustral/clustral-agent:<tag>` into your internal registry, then set:

```bash
--set image.repository=registry.internal/clustral/clustral-agent \
--set image.tag=v0.4.0
```

If your registry requires authentication, create an `imagePullSecret` and reference it via `serviceAccount.imagePullSecrets` on the chart's ServiceAccount.

### Custom CA for the control-plane endpoint

If your control plane uses a certificate issued by your enterprise PKI (not a public CA), mount the trust anchor via `caCertConfigMap`. The control plane's server cert must chain to that CA.

### NetworkPolicy

If your cluster enforces `NetworkPolicy` as default-deny, allow the agent egress:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: clustral-agent-egress
  namespace: clustral-system
spec:
  podSelector:
    matchLabels:
      app.kubernetes.io/name: clustral-agent
  policyTypes: ["Egress"]
  egress:
    - to:
        - ipBlock:
            cidr: 0.0.0.0/0
      ports:
        - protocol: TCP
          port: 5443
    - to:
        - namespaceSelector: {}
      ports:
        - protocol: TCP
          port: 443              # k8s API server
        - protocol: UDP
          port: 53               # cluster DNS
```

### Sidecars

Not supported today. The agent is a single-container pod. If you need to route its telemetry through a sidecar (e.g., a log shipper), layer that at the cluster level, not in the chart.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Pod CrashLoopBackOff, logs show `TOKEN_ALREADY_USED` | Bootstrap token already consumed by a previous install. | Deregister the cluster (`clustral clusters deregister`), re-register to get a fresh token, `helm upgrade --set bootstrapToken=<new>`. |
| Pod CrashLoopBackOff, logs show `context deadline exceeded` on `RegisterAgent` | Control plane unreachable on `:5443`. | Verify DNS for `controlPlaneUrl` from inside the pod, check firewall rules, confirm the control plane is healthy. |
| Logs show `tls: unknown authority` | Control plane's server certificate is not trusted. | Deploy a cert chained to a CA the agent trusts, or set `caCertConfigMap` to mount the correct trust anchor. |
| `kubectl` returns `AGENT_NOT_CONNECTED` | Agent pod not running or tunnel broken. | `kubectl -n clustral-system logs deploy/clustral-agent` and check for reconnect loops. |
| `kubectl` returns 403 despite correct Clustral role | `ClusterRoleBinding` inside the cluster does not map the impersonated group to real RBAC. | Create a `ClusterRoleBinding` binding the impersonated group (e.g., `clustral:sre`) to a Kubernetes `ClusterRole`. |
| `ClusterRole` missing after install | `--set rbac.create=false` during install. | `helm upgrade --set rbac.create=true` or apply `src/clustral-agent/k8s/clusterrole.yaml` manually. |
| Cluster shows `Disconnected` immediately after install | Readiness delay — the tunnel takes a few seconds to come up. | Wait 15–30 seconds; if it persists, check agent logs. |

## See also

- [mTLS Bootstrap](mtls-bootstrap.md) — details on the bootstrap token exchange and credential lifecycle.
- [Architecture — Tunnel Lifecycle](../architecture/tunnel-lifecycle.md) — how the persistent gRPC stream is opened and maintained.
- [Architecture — Network Map](../architecture/network-map.md) — exact ports and directions to allow in the firewall.
- [Operator Guide — Upgrade](../operator-guide/upgrade.md) — rolling control plane and agents.
- [Security Model](../security-model/README.md) — what trust the agent holds and how to revoke it.
