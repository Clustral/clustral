---
description: Run the full Clustral stack on your machine — infrastructure, control plane, agent against a kind cluster, CLI, and Web UI.
---

# Local development

This walkthrough gets every Clustral component running on your laptop against a local [kind](https://kind.sigs.k8s.io) cluster. Expect it to take about 15 minutes the first time.

## Prerequisites

Install these before you start:

- [Docker Desktop](https://docs.docker.com/desktop/) or [OrbStack](https://orbstack.dev) (for the infrastructure stack and kind)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 20+](https://nodejs.org) and [Bun](https://bun.sh) (for the Web UI)
- [kind](https://kind.sigs.k8s.io/docs/user/quick-start/) and [kubectl](https://kubernetes.io/docs/tasks/tools/)
- [Go 1.23+](https://go.dev/dl/) (for the agent)

{% hint style="info" %}
The walkthrough below assumes macOS or Linux. Windows works with WSL2; use a WSL shell for every command.
{% endhint %}

## 1. Clone the repository

```bash
git clone https://github.com/Clustral/clustral.git
cd clustral
```

## 2. Start backing services

Edit `HOST_IP` in `.env` to match your machine's IP address (`ipconfig getifaddr en0` on macOS, `hostname -I` on Linux). The same value must appear in your browser URL, `NEXTAUTH_URL`, and the OIDC issuer/audience, so the token's `iss` claim matches what the gateway expects.

```bash
docker compose -f infra/docker-compose.yml up -d
```

This starts MongoDB, RabbitMQ, and Keycloak with the Clustral realm pre-imported.

| Service    | Port  | Credentials                          |
|------------|-------|--------------------------------------|
| MongoDB    | 27017 | (no auth in dev)                     |
| RabbitMQ   | 5672  | user=`clustral`, password=`clustral` |
| Keycloak   | 8080  | admin=`admin`, password=`admin`      |

Keycloak takes 20–40 seconds to finish the realm import on first start. Wait until `docker compose logs keycloak | grep "started in"` prints a start-up message before continuing.

## 3. Create the local kind cluster

```bash
kind create cluster --config infra/k8s/kind-config.yaml
```

This creates a single-node kind cluster with the container runtime exposed on the host so the agent can reach it during development.

## 4. Start the ControlPlane

Open a new terminal:

```bash
cd src/Clustral.ControlPlane
dotnet run
```

The ControlPlane listens on HTTP `:5100` (REST, via the gateway) and gRPC mTLS `:5443` (direct to agents).

## 5. Start the API Gateway

Open a new terminal:

```bash
cd src/Clustral.ApiGateway
dotnet run
```

The gateway listens on HTTP `:8080`. It validates OIDC tokens against Keycloak and forwards internal JWTs to the ControlPlane and AuditService.

## 6. Start the AuditService

Open a new terminal:

```bash
cd src/Clustral.AuditService
dotnet run
```

The AuditService listens on HTTP `:5200` and consumes integration events from RabbitMQ.

## 7. Start the Web UI

Open a new terminal:

```bash
cd src/Clustral.Web
bun install
bun dev
```

The Web UI is at [http://localhost:5173](http://localhost:5173). Sign in with the `alice` / `alice` Keycloak user (created by the realm import).

## 8. Start the agent against kind

Open a new terminal:

```bash
cd src/clustral-agent
export AGENT_CONTROL_PLANE_URL=https://localhost:5443
export AGENT_KUBECONFIG=~/.kube/config
export AGENT_KUBE_CONTEXT=kind-clustral-dev
go run .
```

The agent opens an outbound gRPC mTLS tunnel to `:5443` and registers itself with the ControlPlane. Check the ControlPlane logs — you should see `AgentConnected cluster=<id>`.

## 9. Use the CLI

```bash
cd src/Clustral.Cli

# Sign in with Keycloak via OIDC PKCE
dotnet run -- login

# List visible clusters
dotnet run -- clusters list

# Write a kubeconfig entry pointing at the proxy
dotnet run -- kube login clustral-dev

# kubectl traffic now routes through Clustral
kubectl get pods -A
```

## Verify the install

Run these from the CLI directory — all four should succeed:

```bash
dotnet run -- status
dotnet run -- doctor
kubectl get nodes
kubectl auth can-i get pods
```

`clustral status` reports your session, visible clusters, active grants, and platform health in one place. `clustral doctor` runs connectivity probes against the gateway, ControlPlane, and OIDC provider and reports specific failures.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Login succeeds in browser but CLI can't exchange token | `HOST_IP` in `.env` differs from the URL you used to sign in | Make `HOST_IP`, `NEXTAUTH_URL`, and the OIDC `issuer` consistent. |
| Agent logs `tls: bad certificate` | Local internal-JWT / kubeconfig-JWT keys not generated | Run `./infra/internal-jwt/generate.sh` and `./infra/kubeconfig-jwt/generate.sh`. |
| `kubectl` hangs on every call | Agent is not connected | Check ControlPlane logs for `AgentConnected`; if missing, confirm step 8 is running. |
| `401 Unauthorized` on every REST call | OIDC audience mismatch | Confirm the token's `aud` claim matches `OIDC_AUDIENCE` in `.env`. |

## See also

- [On-Prem Docker Compose](on-prem-docker-compose.md) — deploy the same stack on a single VM.
- [Architecture Overview](../architecture/README.md) — how the components fit together.
- [CLI Reference](../cli-reference/README.md) — every `clustral` command.
- [Agent Deployment](../agent-deployment/README.md) — deploy the agent to a real cluster.
