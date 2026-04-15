---
description: Deploy the full Clustral stack on a single Linux VM using Docker Compose — suitable for small teams and pilot deployments.
---

# On-prem Docker Compose

Docker Compose is the simplest way to run Clustral for a small team or a proof-of-concept. Everything except your Kubernetes agents runs on a single Linux VM: nginx, the API Gateway, the ControlPlane, the AuditService, the Web UI, MongoDB, RabbitMQ, and — for this quickstart — Keycloak as the identity provider.

{% hint style="info" %}
For larger deployments or production, run the same containers on a managed container platform (ECS, Nomad, or Kubernetes itself) and point them at an external MongoDB and RabbitMQ. The configuration surface is identical.
{% endhint %}

## System requirements

| Resource  | Minimum    | Recommended |
|-----------|-----------|-------------|
| CPU       | 2 cores    | 4 cores      |
| Memory    | 4 GB       | 8 GB         |
| Disk      | 20 GB      | 50 GB        |
| OS        | Linux kernel 5.10+ | Ubuntu 22.04 / Debian 12 / RHEL 9 |
| Docker    | 24+ with Compose v2 plugin | Latest stable |

Open these ports on the VM firewall:

| Port  | Direction | Purpose |
|-------|-----------|---------|
| 443   | Inbound from users | Web UI + REST API via nginx |
| 5443  | Inbound from agents only | gRPC mTLS tunnel (direct to ControlPlane) |
| 8080  | Inbound from users | Keycloak sign-in (remove if you use an external IdP) |

## 1. Clone and configure

```bash
git clone https://github.com/Clustral/clustral.git
cd clustral
```

Edit `.env`. The required changes:

```bash
# Replace with the VM's public IP or hostname
HOST_IP=clustral.example.com

# Set a 32-byte random secret for the Web UI session cookies
AUTH_SECRET=$(openssl rand -hex 32)
```

{% hint style="warning" %}
Every URL in `.env` derives from `HOST_IP`. Users, the CLI, and the Web UI must all reach the VM via that exact hostname — the gateway strictly validates the OIDC `iss` claim, which comes from whichever hostname the browser used to sign in.
{% endhint %}

## 2. Generate TLS material

```bash
./infra/ca/generate.sh             # internal CA for agent mTLS
./infra/internal-jwt/generate.sh   # ES256 keypair for gateway → downstream JWTs
./infra/kubeconfig-jwt/generate.sh # ES256 keypair for kubeconfig JWTs
./infra/nginx/generate-tls.sh      # self-signed cert for nginx; replace for production
```

For production, replace `infra/nginx/certs/` with a real certificate from a CA your users trust (Let's Encrypt, your internal PKI, or a commercial issuer).

## 3. Start the infrastructure stack

```bash
docker compose -f infra/docker-compose.yml up -d
```

This starts MongoDB, RabbitMQ, Keycloak (with the Clustral realm pre-imported), and nginx. Wait for Keycloak to finish importing the realm:

```bash
docker compose -f infra/docker-compose.yml logs -f keycloak
# Look for: "started in NN.NNNs"
```

## 4. Start the application stack

```bash
docker compose up -d
```

This builds and starts the API Gateway, ControlPlane, AuditService, and Web UI. The first run compiles Docker images from source, which takes 3–5 minutes.

## 5. Verify

Browse to `https://<HOST_IP>/` — the Web UI should load. Sign in with the pre-seeded Keycloak user `alice` / `alice`.

Health endpoints:

```bash
curl -k https://<HOST_IP>/healthz          # ControlPlane liveness
curl -k https://<HOST_IP>/healthz/ready    # ControlPlane readiness (MongoDB ping)
```

Both should return `200 OK`.

## 6. Swap in your real identity provider

Keycloak is bundled for convenience but not required. To use an external IdP (Auth0, Okta, Azure AD):

1. Register two OIDC clients at your IdP:
   - `clustral-control-plane` — audience claim for the gateway (Authorization Code + PKCE, public client for the CLI)
   - `clustral-web` — confidential client for server-side NextAuth (Authorization Code, client secret)
2. Add an audience mapper on both so the token's `aud` claim contains `clustral-control-plane`.
3. Update `.env`:

   ```bash
   OIDC_AUTHORITY=https://idp.example.com
   OIDC_METADATA_ADDRESS=https://idp.example.com/.well-known/openid-configuration
   OIDC_AUDIENCE=clustral-control-plane
   OIDC_REQUIRE_HTTPS=true
   OIDC_WEB_ISSUER=https://idp.example.com
   OIDC_WEB_CLIENT_ID=clustral-web
   OIDC_WEB_CLIENT_SECRET=<from-your-idp>
   ```

4. `docker compose up -d --force-recreate api-gateway web`
5. Remove the Keycloak container from `infra/docker-compose.yml` if you do not need it.

See [Authentication Flows](../architecture/authentication-flows.md) for what the gateway verifies on every token.

## 7. Deploy the first agent

On a machine with `kubectl` access to your target cluster:

```bash
clustral login
clustral clusters register my-cluster
# → outputs a bootstrap token
helm install clustral-agent ./infra/helm \
  --set controlPlaneUrl=https://<HOST_IP>:5443 \
  --set bootstrapToken=<token>
```

See [Agent Deployment → Helm Chart](../agent-deployment/helm-chart.md) for the full values reference.

## Verify end-to-end

```bash
clustral login
clustral kube login my-cluster
kubectl get pods -A
```

If the last command returns pods, your installation is healthy.

## Upgrading

```bash
git fetch origin && git pull
docker compose pull
docker compose up -d
```

For major version upgrades, read [Operator Guide → Upgrade](../operator-guide/upgrade.md) first — some releases include schema migrations that must run ahead of image rollout.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Browser shows "connection refused" on port 443 | nginx container failed to start (TLS cert missing) | Regenerate with `./infra/nginx/generate-tls.sh` or mount your real cert at `infra/nginx/certs/tls.{crt,key}`. |
| Login redirects to IdP, then back to `/error` | OIDC audience mismatch | Confirm `OIDC_AUDIENCE` matches the `aud` claim your IdP issues. |
| Gateway logs `JWT validation failed: invalid issuer` | `HOST_IP` changed after users cached tokens | Have users sign out and back in, or add the old hostname to `Oidc:ValidIssuers` during the cutover. |
| Agent registered but clusters list is empty | Gateway reached but ControlPlane isn't | Check `docker compose ps` — ControlPlane must be `healthy`. |
| Web UI shows "audit unavailable" | AuditService not consuming RabbitMQ | Check `docker compose logs audit-service` for connection errors. |

## See also

- [Local Development](local-development.md) — run components natively for debugging.
- [Agent Deployment](../agent-deployment/README.md) — deploy agents into your target clusters.
- [Operator Guide](../operator-guide/README.md) — upgrade, monitoring, backup.
- [Security Model](../security-model/README.md) — threat model and key handling before production.
