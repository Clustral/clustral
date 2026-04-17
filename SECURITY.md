# Security Policy

## Supported Versions

Clustral follows semantic versioning. Security patches are applied to the latest minor release only. If you are running an older version, upgrade to receive fixes.

| Version | Supported          |
| ------- | ------------------ |
| latest  | :white_check_mark: |
| < latest | :x:               |

Once Clustral reaches v1.0, this table will expand to cover LTS branches.

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, report vulnerabilities privately via [GitHub Security Advisories](https://github.com/Clustral/clustral/security/advisories/new).

### What to include

- A description of the vulnerability and its potential impact.
- Steps to reproduce or a proof-of-concept (if available).
- The component affected (ControlPlane, API Gateway, Agent, CLI, Web UI, SDK).
- The version(s) you tested against.

### What to expect

| Milestone | Timeline |
|---|---|
| Acknowledgment | Within 48 hours of report |
| Initial assessment (severity, affected versions) | Within 5 business days |
| Fix development + private disclosure to reporter | Within 30 days for critical/high; 90 days for medium/low |
| Public advisory + patched release | Coordinated with reporter; default is on fix release day |

If the report is accepted, you will be credited in the advisory (unless you prefer to remain anonymous). If declined, you will receive an explanation of why.

### Severity classification

Clustral uses [CVSS v3.1](https://www.first.org/cvss/calculator/3.1) for severity scoring:

| Severity | CVSS Score | Examples |
|---|---|---|
| **Critical** | 9.0–10.0 | Remote code execution, authentication bypass, CA private key exposure |
| **High** | 7.0–8.9 | Privilege escalation, credential theft, tunnel session hijacking |
| **Medium** | 4.0–6.9 | Information disclosure (non-credential), CSRF, denial of service |
| **Low** | 0.1–3.9 | Debug information leakage, timing side-channels with limited impact |

## Security Architecture

Clustral's security model is documented in detail:

- [Security Model — mTLS & JWT Lifecycle](https://docs.clustral.kube.it.com/security-model/mtls-jwt-lifecycle) — three-JWT architecture, key rotation procedures, threat model, blast-radius analysis per key type.
- [Security Model — Audit Log](https://docs.clustral.kube.it.com/security-model/audit-log) — what is logged, event taxonomy, retention, compliance considerations.
- [Architecture — Authentication Flows](https://docs.clustral.kube.it.com/architecture/authentication-flows) — OIDC PKCE, kubeconfig JWT exchange, proxy auth chain.
- [Architecture — Network Map](https://docs.clustral.kube.it.com/architecture/network-map) — ports, directions, firewall rules, zero-inbound-ports agent design.

### Key trust boundaries

| Boundary | What protects it |
|---|---|
| User → Platform | OIDC JWT (validated by API Gateway) |
| kubectl → Proxy | Kubeconfig JWT (ES256, 8h TTL, revocable) |
| Gateway → Downstream services | Internal JWT (ES256, 30s TTL) |
| Agent → ControlPlane | mTLS client certificate (Clustral CA) + RS256 JWT |
| ControlPlane → Agent | gRPC over the same mTLS stream (bidirectional) |

### Credential lifecycle

- **OIDC tokens** — issued by your identity provider; Clustral validates but never stores them.
- **Kubeconfig JWTs** — signed by the ControlPlane (ES256), SHA-256 hash stored for revocation lookup. Revocable immediately via `POST /api/v1/auth/revoke-by-token`.
- **Internal JWTs** — 30-second TTL, never persisted, never leave the internal network.
- **Agent mTLS certificates** — issued by the Clustral CA on bootstrap (single-use token), auto-renewed 30 days before expiry. Revocation is enforced at the ControlPlane authorization layer, not via CRL.

## Hardening Checklist

Before deploying Clustral to production:

- [ ] Replace self-signed TLS certificates with certificates from a trusted CA (Let's Encrypt, internal PKI).
- [ ] Set `oidc.requireHttps: true` (default) — never disable in production.
- [ ] Restrict the gRPC port (`:5443`) source CIDR to your agent networks only.
- [ ] Enable `NetworkPolicy` (default in the Helm chart) to enforce pod-to-pod isolation.
- [ ] Store signing keys in a secrets manager (HashiCorp Vault, AWS Secrets Manager) rather than on disk — or use the cert-manager integration (Helm chart default).
- [ ] Configure audit log retention per your compliance framework (SOC 2, ISO 27001, HIPAA).
- [ ] Review the [key rotation calendar](https://docs.clustral.kube.it.com/security-model/mtls-jwt-lifecycle#key-rotation-calendar-recommended) and schedule rotations.
- [ ] Enable rate limiting (default: 100 rps / 200 burst per credential).
- [ ] Run `clustral doctor` from a client to verify TLS, OIDC, and tunnel connectivity.

## Dependencies

Clustral's dependency tree is tracked via:

- **NuGet** (`Directory.Packages.props`) — .NET packages, centrally versioned.
- **Go modules** (`go.sum`) — Agent dependencies.
- **npm/bun** (`bun.lockb`) — Web UI dependencies.
- **Docker base images** — pinned in each `Dockerfile`.

Dependabot is enabled on this repository. Security advisories for transitive dependencies trigger automatic PRs.
