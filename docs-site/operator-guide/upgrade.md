---
description: Rolling upgrades for Clustral — version scheme, compatibility policy, component order, and rollback.
---

# Upgrade

Upgrades happen in place by pulling new images and restarting components in the right order.

## Overview

Clustral ships via git-tag-driven releases. Every component has a Docker image tagged with the semver version and a set of floating channel tags. You upgrade by bumping the image tag in your orchestrator — `docker compose pull && docker compose up -d` for Docker Compose, a Helm chart value bump for the agent, a `kubectl set image` or manifest bump for Kubernetes.

There is no separate `clustral upgrade` command. Every component is stateless except the ControlPlane, which owns MongoDB. Migrations are idempotent and run automatically when the ControlPlane starts. There is no data path that requires all components to restart together.

{% hint style="info" %}
Read the release notes for every release between your current and target version — not only the target. Some releases bundle operational steps (index rebuilds, retention cleanup, OIDC config changes) that you must not skip over.
{% endhint %}

## Version scheme

Clustral uses semver (`MAJOR.MINOR.PATCH`) with pre-release channels.

| Tag | Meaning | Updated on |
|---|---|---|
| `<version>` | Exact release (e.g. `0.5.2`) | Immutable once pushed. |
| `<major>.<minor>` | Floating patch for a minor (e.g. `0.5`) | Every stable patch release in that minor. |
| `<major>` | Floating minor for a major (e.g. `0`) | Every stable release in that major. |
| `latest` | Newest stable release | Every stable release. |
| `alpha` / `beta` / `rc` | Newest pre-release on that channel | Every pre-release on that channel. |

Pre-release tags (`-alpha.N`, `-beta.N`, `-rc.N`) only move their channel float. They never update `latest`, `<major>`, or `<major>.<minor>`. Pin the exact version (`0.6.0-beta.3`) in production if you're tracking a pre-release — channel tags move under you.

Git tags are the sole version source. Components derive their version from `git describe --tags` at build time and expose it via:

- ControlPlane: `GET /api/v1/version` (unauthenticated).
- ControlPlane authenticated detail: `GET /healthz/detail`.
- CLI: `clustral version` (prints CLI version + ControlPlane version).
- Agent: reported in the `AgentHello` gRPC handshake and stored on `Cluster.AgentVersion`.

## Compatibility policy

Stated explicitly so you can plan rollouts.

| Direction | Rule |
|---|---|
| Within a minor (e.g. `0.5.1` → `0.5.7`) | All components are interchangeable. Order doesn't matter. |
| Across minor (e.g. `0.5.x` → `0.6.0`) | Follow the upgrade order below. Agents can lag the ControlPlane by at most one minor. |
| Across major (e.g. `1.x` → `2.0`) | Read the release notes. A major bump may introduce breaking wire changes that require a coordinated rollout. |

Agent skew table:

| Skew | Supported | Notes |
|---|---|---|
| Agent == ControlPlane | Yes | Default. |
| Agent one minor behind ControlPlane | Yes | Deprecation warnings in ControlPlane logs. Visible in `clustral clusters list`. |
| Agent two minors behind | No | ControlPlane may reject old proto versions. Upgrade agents before stepping the ControlPlane past their lagging minor. |

The ControlPlane logs a warning on every heartbeat from an agent whose version differs from its own. Use `clustral clusters list` to scan for lagging agents before a minor bump.

## Pre-upgrade checklist

Run through this before every production upgrade.

- [ ] Read the release notes for every release between your current and target version (not just the target).
- [ ] Take a MongoDB backup of the `clustral` and `clustral-audit` databases. `mongodump --uri "$MONGO_CONNECTION_STRING" --archive=backup-$(date +%F).gz --gzip` is enough.
- [ ] Verify all agents are on a supported version for the target — no agent more than one minor behind the target ControlPlane version.
- [ ] Snapshot the current running image digests for rollback: `docker compose images --quiet | sort > pre-upgrade-digests.txt`.
- [ ] Announce a maintenance window if any agents might restart during the upgrade. Active `kubectl` connections through a restarting agent break and the client retries; long watches (`kubectl logs -f`, `kubectl port-forward`) need to be restarted.
- [ ] Confirm `ERRORS_DOCS_BASE_URL` points at the right docs mirror if you run an air-gapped docs site — release notes occasionally shift error codes to new pages.

## Upgrade procedure — Docker Compose

This is the reference path. Adapt the order to your orchestrator; the rules are the same.

1.  **Pull release-shipped config changes:**
    ```bash
    git fetch origin
    git checkout <release-tag>   # or main if you track a channel
    ```
    Review `.env.example` and the `docker-compose.yml` diff for any new variables. Copy new defaults into your environment-specific `.env`.

2.  **Apply schema migrations ahead of the image swap (production).** The ControlPlane auto-applies EF Core migrations on startup in Development, but for production generate and apply the idempotent SQL script yourself so you can review what runs:
    ```bash
    dotnet ef migrations script --idempotent \
        --project src/Clustral.ControlPlane \
        -o infra/migrations/latest.sql
    ```
    Apply via your normal migration runner. All Clustral migrations are additive — no destructive operations on existing documents. Custom retention cleanup jobs you've added may not be idempotent; run them before or after, not during.

3.  **Pull new images:**
    ```bash
    docker compose pull
    ```

4.  **Upgrade ControlPlane first:**
    ```bash
    docker compose up -d controlplane
    ```
    The ControlPlane owns MongoDB and signs kubeconfig JWTs. Upgrading it first means newer wire contracts are available before newer clients (Gateway, Web) try to use them.

5.  **Wait for readiness:**
    ```bash
    until curl -fs http://localhost:5100/healthz/ready; do sleep 2; done
    ```
    `/healthz/ready` returns 200 once MongoDB is reachable and the ControlPlane has finished startup migrations.

6.  **Upgrade AuditService:**
    ```bash
    docker compose up -d audit-service
    ```
    Wait for `curl -fs http://localhost:5200/swagger` to respond (the Docker healthcheck probes this). The AuditService consumes RabbitMQ messages, so a brief outage causes a queue backlog — that's fine; MassTransit redelivers.

7.  **Upgrade API Gateway:**
    ```bash
    docker compose up -d api-gateway
    ```
    Wait for `curl -fs http://localhost:8080/gateway/healthz/ready` to return 200. The readiness probe verifies OIDC provider connectivity.

8.  **Upgrade Web UI last:**
    ```bash
    docker compose up -d web
    ```
    The Web UI calls the Gateway and AuditService, so upgrade it after both. No health endpoint — verify by loading `https://<HOST_IP>/` in a browser.

9.  **Upgrade agents separately.** Agents run inside target clusters via Helm and upgrade on their own schedule. See [Agent Deployment](../agent-deployment/helm-chart.md).

{% hint style="warning" %}
Do not upgrade the Gateway before the ControlPlane. The Gateway issues internal JWTs the ControlPlane must validate; a newer Gateway may add claims the older ControlPlane rejects.
{% endhint %}

## Verifying the upgrade

After every component is running, check:

```bash
# Versions
curl -fs http://localhost:5100/api/v1/version

# ControlPlane can sign + validate the internal JWT pipeline
curl -fs http://localhost:8080/gateway/healthz/ready

# Agents still connected
# (from any host with a valid token)
clustral clusters list

# Audit pipeline still ingesting
curl -fs http://localhost:5200/swagger > /dev/null && echo "audit ok"
```

Then run a smoke test from a CLI:

```bash
clustral login
clustral status
clustral kube login <any-cluster>
kubectl get namespaces
```

If `kubectl get namespaces` returns, the full proxy chain (Gateway → ControlPlane → tunnel → agent → k8s API) is working.

## Rollback procedure

If a component fails to come up healthy, roll back immediately — do not leave a partially upgraded stack running.

1.  **Pin to the previous image digest** from `pre-upgrade-digests.txt`:
    ```bash
    docker compose up -d --force-recreate <service>
    ```
    With the old `image:` tag or digest in `docker-compose.yml`. If you use a channel tag (`latest`), switch to the pinned version for the rollback window.

2.  **Restore MongoDB if a migration landed:**
    ```bash
    mongorestore --uri "$MONGO_CONNECTION_STRING" \
        --archive=backup-YYYY-MM-DD.gz --gzip --drop
    ```
    Clustral migrations are additive, so reverting the image without reverting the database is safe for additive-only changes — the older ControlPlane ignores the new fields. Restore only if the release notes mark a migration as breaking or if you see deserialization errors on the older image.

3.  **Verify rollback health** with the same checks from the "Verifying the upgrade" section.

4.  **File an issue** at [github.com/Clustral/clustral/issues](https://github.com/Clustral/clustral/issues) with the failing component logs, the target version, and the version you rolled back to.

{% hint style="danger" %}
Never edit applied migration files and re-run them. If a migration misbehaves, roll back to the previous image and restore the database dump. Add a new forward migration in the next release to correct state.
{% endhint %}

## Agent rollout

Agents live in target clusters and upgrade via Helm:

```bash
helm repo update
helm upgrade clustral-agent clustral/agent \
    --namespace clustral-system \
    --values values.yaml \
    --version <chart-version>
```

Rollouts happen per cluster, not centrally. The ControlPlane continues serving connections from older agents as long as they're within one minor of the current ControlPlane version. Plan agent upgrades cluster-by-cluster and watch for `AgentConnected` / `AgentDisconnected` events in the audit log.

See [Agent Deployment — Helm Chart](../agent-deployment/helm-chart.md) for chart values, upgrade strategy, and mTLS handling.

## Production deployment variants

Clustral ships Docker Compose as its reference deployment. Other shapes are supported with caveats.

### Kubernetes

First-party Helm charts for the control plane are not yet published (the agent chart is). Operators typically build their own charts from the Docker Compose spec — the image list and environment variables map 1:1. The upgrade rules in this page apply unchanged: upgrade ControlPlane first, then AuditService, Gateway, Web. Use a rolling update strategy with `maxUnavailable: 0` for the ControlPlane so the Gateway never sees a gap.

### systemd on a single VM

Run each component as a systemd unit wrapping `docker run` or the native binary. Upgrade by bumping the image tag in the unit file and `systemctl restart <unit>`. Apply the same order. The only extra step is ensuring the unit waits on MongoDB before starting — add `Requires=docker.service` and a `ExecStartPre` that probes MongoDB.

## See also

- [Monitoring](monitoring.md) — watch health, logs, and metrics during the upgrade.
- [Troubleshooting](troubleshooting.md) — common upgrade failures and their fixes.
- [On-Prem Docker Compose](../getting-started/on-prem-docker-compose.md) — initial install on a single VM.
- [Agent Deployment — Helm Chart](../agent-deployment/helm-chart.md) — agent-side rollouts.
