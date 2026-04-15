---
description: Common questions and first-line troubleshooting for Clustral end users, operators, and security reviewers.
---

# FAQ & Troubleshooting

Short answers to the questions that come up most often. Each entry links to the page that covers it in full.

## For end users

### What does Clustral actually do when I run `kubectl`?

Your request goes to the Clustral proxy, through an outbound gRPC tunnel, to the agent in the target cluster, and finally to the local Kubernetes API. The agent impersonates you based on your role grants — Clustral never stores cluster credentials on your laptop. See [Authentication Flows](../architecture/authentication-flows.md).

### My kubectl hangs forever. What do I do?

The most common cause is that the agent in the target cluster is disconnected. Run `clustral status` — any cluster marked `Disconnected` cannot serve traffic. Escalate to whoever operates your Clustral deployment. See [Troubleshooting](../operator-guide/troubleshooting.md).

### How often do I need to log in?

Your OIDC session lasts for whatever your identity provider configures, typically 30 minutes to 8 hours. The kubeconfig credential that `clustral kube login` writes lasts 8 hours by default and is independent — it keeps working after your OIDC session expires, up to its own TTL. See [`clustral kube login`](../cli-reference/kube-login.md).

### I keep getting `no active role assignment on cluster X`. How do I get access?

You need a role grant on that cluster. Request one with `clustral access request --cluster X --role <role-name>` and wait for an admin to approve. See [`clustral access`](../cli-reference/access.md).

### Can I use multiple clusters at once?

Yes. Run `clustral kube login` for each cluster; each gets its own kubeconfig context. Switch between them with `kubectl config use-context clustral-<cluster-name>`. See [`clustral kube login`](../cli-reference/kube-login.md).

## For operators

### How do I add a new cluster?

Run `clustral clusters register <name>` on the control plane side to get a single-use bootstrap token, then install the agent Helm chart in the target cluster with that token. See [Agent Deployment — Helm Chart](../agent-deployment/helm-chart.md).

### How do I swap to a different identity provider?

Register two OIDC clients at the new IdP (one for the control plane audience, one for the Web UI), update the `OIDC_*` variables in `.env`, and restart the gateway and Web containers. Users sign out and sign back in. See [On-Prem Docker Compose](../getting-started/on-prem-docker-compose.md).

### How do I change the default kubeconfig credential TTL?

Set `CREDENTIAL_DEFAULT_TTL` and `CREDENTIAL_MAX_TTL` in `.env` and restart the ControlPlane. Users can then request specific TTLs with `clustral kube login --ttl PT4H` up to the configured maximum. See [`clustral kube login`](../cli-reference/kube-login.md).

### How do I back up and restore Clustral?

Take a MongoDB dump of both databases: `mongodump --db clustral` and `mongodump --db clustral-audit`, and restore with `mongorestore`. Back up the signing keys in `infra/internal-jwt/`, `infra/kubeconfig-jwt/`, and `infra/ca/` separately — losing them forces a key rotation and agent re-bootstrap. See [Security — mTLS & JWT Lifecycle](../security-model/mtls-jwt-lifecycle.md).

### How do I rotate TLS certificates before they expire?

nginx's server certificate is standard TLS — rotate by replacing the files in `infra/nginx/certs/`. The internal CA used for agent mTLS is separate and rotating it requires re-bootstrapping every agent, so plan carefully. See [Security — mTLS & JWT Lifecycle](../security-model/mtls-jwt-lifecycle.md).

## For security reviewers

### What does Clustral log to the audit trail?

Every authentication, access-request state change, credential issuance and revocation, cluster registration, and significant proxy error. Request bodies are not captured — only metadata such as path, verb, status, cluster, and user. See [Security — Audit Log](../security-model/audit-log.md).

### How long is audit data retained?

There is no built-in retention today; records accumulate indefinitely. Operators typically apply a MongoDB TTL index on `clustral-audit.audit_events.timestamp` or export to cold storage on a schedule. Confirm your compliance framework's minimum retention before enabling TTL. See [Security — Audit Log](../security-model/audit-log.md).

### How do you handle a suspected signing-key compromise?

Each key type has its own rotation procedure. The internal JWT and kubeconfig JWT keys use two-key overlap for zero-downtime rotation; the CA requires re-bootstrapping every agent. See [Security — mTLS & JWT Lifecycle](../security-model/mtls-jwt-lifecycle.md) and the project's `SECURITY.md` for how to report a vulnerability privately.

## See also

- [CLI Reference](../cli-reference/README.md) — for command-level questions.
- [Troubleshooting](../operator-guide/troubleshooting.md) — the deep operator-side diagnostic page.
- [Error Reference](../errors/README.md) — the full error-code catalog.
- [Security Model](../security-model/README.md) — deeper security context.
