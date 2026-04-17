---
description: Deploy the full Clustral platform on Kubernetes using the official Helm chart.
---

# Kubernetes Deployment

Deploy Clustral on a Kubernetes cluster with a single Helm chart. The `clustral` chart installs all platform components — ControlPlane, API Gateway, Web UI, AuditService, nginx, MongoDB, and RabbitMQ. cert-manager generates all TLS and JWT secrets by default, so you do not need to manage key material manually.

## Prerequisites

- **Helm 3.12+** with OCI registry support.
- **kubectl** pointed at the target cluster with cluster-admin permissions.
- **cert-manager** installed on the cluster. The chart uses cert-manager `Certificate` and `Issuer` resources to generate CA, TLS, and JWT key pairs automatically. See [Without cert-manager](#without-cert-manager) if you cannot install it.
- An **OIDC provider** (Keycloak, Auth0, Okta, Azure AD) with a client configured for Clustral. See [Configure OIDC](#configure-oidc) for details.
- A **default StorageClass** for MongoDB and RabbitMQ persistent volumes (or disable persistence for evaluation).

Install cert-manager if you do not already have it:

```bash
kubectl apply -f https://github.com/cert-manager/cert-manager/releases/latest/download/cert-manager.yaml
kubectl -n cert-manager wait --for=condition=Available deployment --all --timeout=120s
```

## Add the Helm repository

```bash
helm repo add clustral https://clustral.github.io/clustral
helm repo update
```

## Install

```bash
helm install clustral clustral/clustral \
  --namespace clustral --create-namespace \
  --set global.domain=clustral.example.com \
  --set oidc.authority=https://idp.example.com/realms/clustral \
  --set oidc.clientId=clustral-control-plane \
  --set oidc.web.clientId=clustral-web \
  --set oidc.web.clientSecret=<your-client-secret> \
  --set web.env.authSecret=$(openssl rand -base64 32)
```

The `global.domain` value controls the Ingress hostname and the URLs that all components advertise. All other required values are OIDC-related.

For a production deployment, you likely want to pin the chart version:

```bash
helm install clustral clustral/clustral --version 1.0.0 \
  --namespace clustral --create-namespace \
  # ... values as above ...
```

To customize further, extract the default values and create a values file:

```bash
helm show values clustral/clustral > my-values.yaml
# Edit my-values.yaml, then:
helm install clustral clustral/clustral \
  --namespace clustral --create-namespace \
  -f my-values.yaml
```

## Verify

Check that all pods reach `Running`:

```bash
kubectl -n clustral get pods
```

You should see pods for `controlplane`, `api-gateway`, `web`, `audit-service`, `nginx`, `mongodb`, and `rabbitmq`.

Run the built-in Helm test (deploys a short-lived pod that hits the health endpoints):

```bash
helm test clustral -n clustral
```

Confirm the Web UI is reachable:

```bash
curl -s https://clustral.example.com/.well-known/clustral-configuration | jq .
```

## Configure OIDC

The OIDC provider configuration is the same whether you deploy with Docker Compose or Helm. You need two clients:

1. **Control Plane client** (`oidc.clientId`) — public client, Authorization Code + PKCE, used by the CLI.
2. **Web client** (`oidc.web.clientId` / `oidc.web.clientSecret`) — confidential client, used by the Web UI's server-side NextAuth integration.

If you use Keycloak, import the realm export from `infra/keycloak/` — it has both clients pre-configured. See [On-Prem Docker Compose](../getting-started/on-prem-docker-compose.md) for the full OIDC setup walkthrough.

## Deploy the first agent

Register a cluster from a machine where you are already signed in with `clustral login`:

```bash
clustral clusters register my-cluster
# Output:
#   clusterId:      a3f7c1e0-...
#   bootstrapToken: bst_ey...
```

Install the agent chart in the target cluster:

```bash
helm install clustral-agent clustral/clustral-agent \
  --namespace clustral-system --create-namespace \
  --set controlPlaneUrl=clustral.example.com:5443 \
  --set clusterId=a3f7c1e0-... \
  --set bootstrapToken=bst_ey...
```

Verify the agent connects:

```bash
clustral clusters list
# Expect: my-cluster  Connected  <agent-version>  <k8s-version>
```

See [Agent Deployment — Helm Chart](../agent-deployment/helm-chart.md) for the full agent values reference, RBAC details, and troubleshooting.

## Gateway API

By default the chart creates an `Ingress` resource for HTTP traffic. If your cluster uses the Kubernetes Gateway API instead, disable Ingress and enable Gateway API:

```bash
helm install clustral clustral/clustral \
  --namespace clustral --create-namespace \
  --set ingress.enabled=false \
  --set gatewayApi.enabled=true \
  --set gatewayApi.gatewayName=my-gateway \
  --set global.domain=clustral.example.com \
  # ... OIDC values ...
```

Ingress and Gateway API are mutually exclusive — the chart validates this and fails if both are enabled.

The Gateway API route is created as an `HTTPRoute` attached to the named `Gateway`. You are responsible for deploying the Gateway resource and its controller (e.g., Envoy Gateway, Istio, Cilium).

Note that gRPC traffic (port 5443) is always exposed via a dedicated Kubernetes Service, regardless of whether you use Ingress or Gateway API. Persistent gRPC streams do not work reliably through L7 proxies.

## Without cert-manager

If you cannot install cert-manager, disable it and generate the secrets manually:

```bash
# Generate all required secrets into the target namespace
cd charts/clustral
./generate-secrets.sh --namespace clustral

# Install with cert-manager disabled
helm install clustral clustral/clustral \
  --namespace clustral --create-namespace \
  --set certManager.enabled=false \
  --set global.domain=clustral.example.com \
  # ... OIDC values ...
```

The `generate-secrets.sh` script creates four Kubernetes Secrets:

| Secret | Contents |
|---|---|
| `clustral-ca` | Self-signed CA certificate + key (mTLS root of trust) |
| `clustral-internal-jwt` | ES256 key pair for internal JWTs (gateway to downstream) |
| `clustral-kubeconfig-jwt` | ES256 key pair for kubeconfig JWTs (ControlPlane to gateway) |
| `clustral-tls` | TLS certificate + key for nginx (signed by the CA) |

## Values reference

View all available values:

```bash
helm show values clustral/clustral
```

See [charts/README.md](https://github.com/Clustral/clustral/blob/main/charts/README.md) for the full values reference with descriptions and defaults.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Pods stuck in `Init` or `Pending` | cert-manager not installed or not ready. | Install cert-manager: `kubectl apply -f https://github.com/cert-manager/cert-manager/releases/latest/download/cert-manager.yaml` and wait for its pods to be ready. |
| Web UI returns 401 / OIDC redirect fails | OIDC audience mismatch between the IdP client and the `oidc.clientId` value. | Verify that `oidc.authority`, `oidc.clientId`, and `oidc.web.clientId` match your IdP configuration exactly. Check redirect URIs include `https://<domain>/api/auth/callback/keycloak`. |
| Agent shows `context deadline exceeded` | gRPC port 5443 not reachable from the agent cluster to the platform cluster. | The ControlPlane gRPC port (5443) is exposed via a dedicated Service, not through Ingress. Verify the Service has an external IP or LoadBalancer, and firewall rules allow TCP 5443. |
| MongoDB or RabbitMQ pods in CrashLoopBackOff | Persistent volume provisioner not available. | Ensure a default `StorageClass` exists. For testing, use `--set mongodb.persistence.enabled=false --set rabbitmq.persistence.enabled=false`. |
| `helm test` fails with connection refused | Services not yet ready. | Wait 30-60 seconds after install. Check `kubectl -n clustral get pods` — all pods should be `Running` before testing. |

## Upgrading

```bash
helm upgrade clustral clustral/clustral \
  --namespace clustral --reuse-values \
  --set image.tag=v1.1.0
```

Follow the **control plane first, agents second** upgrade order. See [Upgrade](upgrade.md) for the full procedure and version compatibility matrix.

## See also

- [On-Prem Docker Compose](../getting-started/on-prem-docker-compose.md) — deploy with Docker Compose instead of Kubernetes.
- [Agent Deployment — Helm Chart](../agent-deployment/helm-chart.md) — deploy the agent in target clusters.
- [Upgrade](upgrade.md) — rolling upgrades for the platform and agents.
- [Security Model](../security-model/README.md) — trust boundaries, mTLS, and JWT lifecycle.
