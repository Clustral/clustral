# Clustral Helm Charts

Two Helm charts for deploying Clustral on Kubernetes.

| Chart | What it deploys |
|---|---|
| `clustral` | The full platform (ControlPlane, API Gateway, AuditService, Web UI, MongoDB, RabbitMQ) |
| `clustral-agent` | The Go agent into a target cluster |

## Prerequisites

- Helm 3.12+
- kubectl with cluster-admin access
- [cert-manager](https://cert-manager.io) installed on the target cluster (default install generates all TLS/JWT secrets automatically). If you don't use cert-manager, see [Without cert-manager](#without-cert-manager) below.

## Add the Helm repository

```bash
helm repo add clustral https://clustral.github.io/clustral
helm repo update
```

## Install the platform

```bash
helm install clustral clustral/clustral \
  --namespace clustral --create-namespace \
  --set global.domain=clustral.example.com \
  --set oidc.authority=https://idp.example.com \
  --set oidc.clientId=clustral-control-plane \
  --set oidc.web.clientId=clustral-web \
  --set oidc.web.clientSecret=<from-your-idp> \
  --set web.env.authSecret=$(openssl rand -base64 32)
```

cert-manager generates the CA, internal JWT, kubeconfig JWT, and TLS secrets automatically. No manual key generation required.

Verify:

```bash
kubectl -n clustral get pods
# All pods should reach Ready within 2-3 minutes

helm test clustral -n clustral
# Runs health checks against all components
```

## Install the agent

On a machine with `kubectl` access to the target cluster:

```bash
# Register the cluster (from a signed-in CLI)
clustral clusters register my-cluster
# → bootstrap token: bst_ey...

# Install the agent
helm install clustral-agent clustral/clustral-agent \
  --namespace clustral-system --create-namespace \
  --set controlPlaneUrl=clustral.example.com:5443 \
  --set clusterId=<cluster-id> \
  --set bootstrapToken=bst_ey...
```

## Values reference

Full values documentation:

```bash
helm show values clustral/clustral
helm show values clustral/clustral-agent
```

### Most commonly changed values (platform)

| Value | Description | Default |
|---|---|---|
| `global.domain` | Domain for Ingress/Gateway TLS and CORS | `clustral.example.com` |
| `oidc.authority` | OIDC provider issuer URL | (required) |
| `oidc.clientId` | OIDC client ID / audience | (required) |
| `oidc.web.clientId` | Web UI OIDC client ID | (required) |
| `oidc.web.clientSecret` | Web UI OIDC client secret | (required) |
| `web.env.authSecret` | NextAuth session encryption key | (required) |
| `certManager.enabled` | Auto-generate all secrets via cert-manager | `true` |
| `ingress.enabled` | Create an nginx Ingress | `true` |
| `gatewayApi.enabled` | Create Gateway API HTTPRoute/GRPCRoute instead | `false` |
| `mongodb.enabled` | Deploy Bitnami MongoDB subchart | `true` |
| `rabbitmq.enabled` | Deploy Bitnami RabbitMQ subchart | `true` |

### Most commonly changed values (agent)

| Value | Description | Default |
|---|---|---|
| `controlPlaneUrl` | Control plane gRPC endpoint | (required) |
| `clusterId` | Cluster ID from registration | (required) |
| `bootstrapToken` | Single-use bootstrap token | (required, first install) |
| `image.tag` | Pin to a specific agent version | Chart appVersion |
| `networkPolicy.enabled` | Restrict egress to controlplane + k8s API | `true` |

## Without cert-manager

If your cluster does not run cert-manager, generate the secrets manually:

```bash
./charts/clustral/scripts/generate-secrets.sh \
  --namespace clustral \
  --domain clustral.example.com
```

Then install with cert-manager disabled:

```bash
helm install clustral clustral/clustral \
  --namespace clustral --create-namespace \
  --set certManager.enabled=false \
  --set global.domain=clustral.example.com \
  --set oidc.authority=... \
  --set oidc.clientId=... \
  --set oidc.web.clientId=... \
  --set oidc.web.clientSecret=... \
  --set web.env.authSecret=$(openssl rand -base64 32)
```

## Gateway API (alternative to Ingress)

To use Gateway API instead of nginx Ingress:

```bash
helm install clustral clustral/clustral \
  --set ingress.enabled=false \
  --set gatewayApi.enabled=true \
  --set gatewayApi.gatewayRef.name=my-gateway \
  ...
```

The chart creates `HTTPRoute` and `GRPCRoute` resources attached to your Gateway. You must deploy the Gateway resource separately (e.g., via your Gateway API provider — Envoy Gateway, Istio, Cilium, etc.).

## Upgrading

```bash
helm upgrade clustral clustral/clustral \
  --namespace clustral --reuse-values
```

Upgrade the platform first, then the agents. See [Operator Guide — Upgrade](docs-site/operator-guide/upgrade.md) for the full procedure.

## Uninstalling

```bash
helm uninstall clustral -n clustral
helm uninstall clustral-agent -n clustral-system
```

This does not delete PersistentVolumeClaims (MongoDB/RabbitMQ data). Delete them manually if you want a clean removal.
