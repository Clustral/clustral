# Security Model

Clustral's security architecture covers OIDC authentication, mTLS for agent-to-ControlPlane communication, short-lived kubeconfig credentials, and a full audit trail of all access.

## Pages in this section

- [mTLS and JWT Lifecycle](mtls-jwt-lifecycle.md) -- Certificate authority, agent bootstrap, credential rotation
- [Audit Log](audit-log.md) -- Event-driven audit trail via RabbitMQ and the AuditService
