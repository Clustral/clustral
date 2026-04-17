# Clustral Helm Charts — Claude Code Guide

Two Helm charts under `charts/`: `agent/` (Go agent for target clusters) and `clustral/` (full platform stack).

---

## Directory layout

```
charts/
├── README.md                        ← User-facing quickstart
├── CLAUDE.md                        ← This file
├── agent/
│   ├── Chart.yaml
│   ├── values.yaml                  ← All configurable values with comments
│   ├── templates/
│   │   ├── _helpers.tpl
│   │   ├── deployment.yaml
│   │   ├── serviceaccount.yaml
│   │   ├── clusterrole.yaml
│   │   ├── clusterrolebinding.yaml
│   │   ├── secret.yaml
│   │   ├── networkpolicy.yaml
│   │   ├── poddisruptionbudget.yaml
│   │   └── NOTES.txt
│   └── ci/
│       └── test-values.yaml         ← Minimum values for CI template test
└── clustral/
    ├── Chart.yaml                   ← Bitnami MongoDB + RabbitMQ as conditional deps
    ├── values.yaml                  ← ~250 lines, every operator-facing knob
    ├── templates/
    │   ├── _helpers.tpl             ← Shared helpers + mutual-exclusivity validation
    │   ├── controlplane-deployment.yaml
    │   ├── controlplane-service.yaml
    │   ├── controlplane-grpc-service.yaml  ← LoadBalancer for agent mTLS (separate from Ingress)
    │   ├── api-gateway-deployment.yaml
    │   ├── api-gateway-service.yaml
    │   ├── audit-service-deployment.yaml
    │   ├── audit-service-service.yaml
    │   ├── web-deployment.yaml
    │   ├── web-service.yaml
    │   ├── ingress.yaml             ← Gated on ingress.enabled
    │   ├── gateway-httproute.yaml   ← Gated on gatewayApi.enabled
    │   ├── gateway-grpcroute.yaml   ← Gated on gatewayApi.enabled
    │   ├── networkpolicy.yaml       ← Per-service policies
    │   ├── poddisruptionbudget.yaml ← One PDB per Deployment
    │   ├── servicemonitor.yaml      ← Prometheus Operator (opt-in)
    │   ├── prometheusrule.yaml      ← Alert rules (opt-in)
    │   ├── cert-manager.yaml        ← Generates all 4 secrets (enabled by default)
    │   ├── configmap.yaml
    │   ├── secrets.yaml             ← Comment-only: documents pre-created Secrets
    │   ├── tests/
    │   │   └── test-connection.yaml ← Helm test pod
    │   └── NOTES.txt
    ├── ci/
    │   └── test-values.yaml
    └── scripts/
        └── generate-secrets.sh      ← Fallback for clusters without cert-manager
```

---

## Conventions

### Values contract

- `values.yaml` is the **sole source of truth** for configurable surface. Never hardcode defaults in templates — always reference `{{ .Values.* }}`.
- Use Helm-doc-style comments (`# --`) for every value so `helm show values` reads as documentation.
- Every env var the application consumes must be surfaced as a value. If a new env var is added to an app service, add a corresponding value and wire it in the template.

### Template naming

- `<service>-deployment.yaml` + `<service>-service.yaml` per app service.
- Enterprise-feature templates use the feature name: `networkpolicy.yaml`, `poddisruptionbudget.yaml`, `servicemonitor.yaml`, `prometheusrule.yaml`, `cert-manager.yaml`.

### Enterprise toggles

Features that require CRDs (ServiceMonitor, PrometheusRule, cert-manager, Gateway API) are gated:

```yaml
{{- if .Values.metrics.serviceMonitor.enabled }}
apiVersion: monitoring.coreos.com/v1
kind: ServiceMonitor
...
{{- end }}
```

Default to **enabled** only if the feature works on vanilla Kubernetes or has a soft failure mode. cert-manager is enabled by default because it eliminates the manual secret generation step; ServiceMonitor/PrometheusRule are disabled by default because they require the Prometheus Operator.

### Ingress vs Gateway API

Mutually exclusive. Validated in `_helpers.tpl`:

```yaml
{{- if and .Values.ingress.enabled .Values.gatewayApi.enabled }}
{{- fail "ingress.enabled and gatewayApi.enabled are mutually exclusive" }}
{{- end }}
```

### Secrets handling

The chart **never generates cryptographic material in templates**. Two paths:

1. **cert-manager (default):** `cert-manager.yaml` creates Certificate CRDs → cert-manager generates Secrets with `tls.key` / `tls.crt`. Templates use volume key projection to remap to `private.pem` / `public.pem` as the .NET services expect. The `Es256JwtService.LoadKey` method (in `packages/Clustral.Sdk/Auth/Es256JwtService.cs`) accepts both raw EC PEM and X.509 certificate PEM, so both paths work without template-side extraction.

2. **Manual (`certManager.enabled: false`):** user creates 4 Secrets via `generate-secrets.sh` or manually. Secret names come from `secrets.*` values.

Helpers in `_helpers.tpl` resolve the correct key names (`tls.key` vs `private.pem`) based on `certManager.enabled`.

### gRPC port — separate from Ingress

The ControlPlane's gRPC mTLS port (5443) is exposed via its own Service (`controlplane-grpc-service.yaml`, default `LoadBalancer`). Agents connect directly to it — persistent gRPC streams break on L7 proxies. This Service is always rendered regardless of the Ingress/Gateway API choice.

When Gateway API is enabled, `gateway-grpcroute.yaml` provides an alternative GRPCRoute for clusters whose Gateway implementations handle gRPC natively (Envoy Gateway, Istio). Operators choose one or the other.

---

## Testing

- **CI:** `.github/workflows/helm.yml` runs `helm lint` + `helm template | kubeconform` on PRs touching `charts/`. On `v*` tags, `chart-releaser-action` publishes to GitHub Pages (`https://clustral.github.io/clustral`).
- **chart-testing:** `ct.yaml` at repo root configures `ct lint`.
- **Helm test:** `helm test <release>` runs `tests/test-connection.yaml` — a pod that curls each healthz endpoint from inside the cluster.
- **Test values:** `ci/test-values.yaml` in each chart provides the minimum required values for CI rendering (mock URLs, disabled deps).

---

## Versioning

`version` and `appVersion` in each `Chart.yaml` are stamped by `chart-releaser-action` from the git tag (`v1.2.3` → `1.2.3`). **Do not bump them manually** — they ship as `0.0.0-dev` in source and are overwritten at publish time.

Docker image tags default to `appVersion` (via `{{ .Values.image.tag | default .Chart.AppVersion }}`), so the chart and images release together on the same tag.

---

## Adding a new value

1. Add it to `values.yaml` with a `# --` comment.
2. Reference it in the appropriate template.
3. If it's a required value, add it to `ci/test-values.yaml`.
4. Update `charts/README.md` if it's in the "commonly changed" table.
5. Run `helm lint charts/<chart>` to verify.

## Adding a new enterprise toggle

1. Add the `enabled: false` toggle under the feature key in `values.yaml`.
2. Gate the template with `{{- if .Values.<feature>.enabled }}`.
3. If it requires a CRD, default to `false` and document the prerequisite in NOTES.txt.
4. Add a CI test: set the toggle in `ci/test-values.yaml` (in a separate CI-variant if it needs CRDs that `kubectl apply --dry-run` won't accept).
