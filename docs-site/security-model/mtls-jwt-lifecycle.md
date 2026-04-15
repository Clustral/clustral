---
description: Reference for the three JWT types and one X.509 mTLS trust anchor Clustral issues — purpose, lifetime, rotation, and blast radius.
---

# mTLS and JWT Lifecycle

Clustral issues and validates three JWT types plus one X.509 mTLS trust anchor. Each has a distinct purpose, lifetime, and rotation procedure. This page is the canonical reference for security reviewers before a production deployment.

## Overview

Authentication in Clustral is layered. End users authenticate against your OIDC provider. The API Gateway re-signs the resulting identity as a short-lived internal JWT that downstream services trust. kubectl authenticates with a separate ES256-signed kubeconfig JWT issued by the ControlPlane. Agents authenticate with mTLS client certificates signed by a private CA that Clustral runs itself.

The four trust anchors are independent. A compromise of one does not silently grant the capabilities of another — the API Gateway enforces distinct issuer, audience, and signing-key expectations per scheme, and rejects tokens that cross the boundary.

{% hint style="info" %}
If you are reviewing Clustral for production, read this page together with [Authentication Flows](../architecture/authentication-flows.md) and [Audit Log](audit-log.md). The flows page shows how credentials move between components; this page documents what each credential is and how to rotate it.
{% endhint %}

## The three JWTs — at a glance

| JWT | Purpose | Issuer | Audience | Algorithm | TTL | Signing key location | Public key location |
|---|---|---|---|---|---|---|---|
| OIDC access token | End-user authentication | Your IdP | `clustral-control-plane` | Provider choice (typically RS256) | IdP-configured | At your IdP | Fetched from IdP JWKS |
| Kubeconfig JWT | kubectl authentication to the proxy | `clustral-controlplane` | `clustral-kubeconfig` | ES256 | 8h default, 8h max | `KubeconfigJwt:PrivateKeyPath` on ControlPlane | `KubeconfigJwt:PublicKeyPath` on API Gateway |
| Internal JWT | Service-to-service trust | `clustral-gateway` | `clustral-internal` | ES256 | 30s per request | `InternalJwt:PrivateKeyPath` on API Gateway | `InternalJwt:PublicKeyPath` on ControlPlane + AuditService |

The mTLS trust anchor is tracked separately — see [mTLS — the agent trust anchor](#mtls-the-agent-trust-anchor) below.

## OIDC access token

**What it proves.** The user signed in against your OIDC provider and the provider vouches for their identity and the set of claims it returned.

**Where it is validated.** The API Gateway (`OidcJwt` scheme). The gateway validates `iss`, `aud`, `exp`, and the signature against the provider's JWKS. The `preferred_username` claim — or whatever you set in `Oidc:NameClaimType` — maps to `User.Identity.Name`. Downstream services never see the OIDC token; the gateway strips it after validation.

**What happens when it is revoked or expires.** The user's next request fails at the gateway with `AUTHENTICATION_REQUIRED` or `INVALID_TOKEN`. The CLI treats that response as a signal to run the login flow again.

**Rotation.** OIDC signing keys are your IdP's responsibility. The gateway fetches the JWKS at startup and caches it. When the IdP advertises a new key, the gateway picks it up on the next JWKS refresh. Forced flush: restart the API Gateway.

**Blast radius of a leaked IdP signing key.** An attacker can forge tokens for any user in that realm. Revoke the key at the IdP, restart the API Gateway to flush the JWKS cache, and re-issue credentials to users. Clustral cannot detect this on its own — the IdP is the root of trust for end-user identity.

## Kubeconfig JWT

**What it proves.** The holder authenticated through the CLI's `clustral kube login` flow and is authorized to proxy kubectl traffic to a specific cluster until the token expires or is explicitly revoked.

**Claims.**

| Claim | Meaning |
|---|---|
| `iss` | `clustral-controlplane` |
| `aud` | `clustral-kubeconfig` |
| `sub` | User's Clustral `userId` |
| `cluster_id` | Target cluster GUID |
| `jti` | `AccessToken` credential ID — used for revocation lookup |
| `exp` | Expiry (≤ 8 hours from issue) |
| `kind` | `kubeconfig` — routes the gateway's scheme selector to `KubeconfigJwt` |

**Where it is validated.** The API Gateway validates the ES256 signature, issuer, audience, and expiry under the `KubeconfigJwt` scheme. The ControlPlane's `ProxyAuthService` additionally checks revocation on every proxy request: it looks up the `jti` in the `AccessToken` table and refuses tokens whose `TokenHash` has been revoked.

**Issuance.** `POST /api/v1/auth/kubeconfig-credential` on the ControlPlane. `KubeconfigJwtService` signs the token with the ES256 private key at `KubeconfigJwt:PrivateKeyPath`. TTL comes from `Credential:DefaultKubeconfigCredentialTtl` (default `08:00:00`), capped at `Credential:MaxKubeconfigCredentialTtl` (default `08:00:00`).

**Revocation.** Two paths:

- Explicit — `DELETE /api/v1/auth/credentials/{id}` marks the `AccessToken` as revoked. Effective on the next proxy request.
- By hash — `POST /api/v1/auth/revoke-by-token` accepts the raw token and revokes the matching record. Use this when the user still has the JWT on disk but not the credential ID.

Revocation is immediate for the proxy path because every kubectl request hits `ProxyAuthService`, which checks the revocation table. It is not immediate at the API Gateway itself — the gateway trusts the signature until the `exp` claim. This is acceptable because the gateway cannot forward traffic to a cluster without the ControlPlane accepting the token.

**Rotation procedure.**

1. Generate a new ES256 keypair (`openssl ecparam -genkey -name prime256v1 -noout -out new-private.pem && openssl ec -in new-private.pem -pubout -out new-public.pem`).
2. Configure the API Gateway to accept both the old and new public keys simultaneously. `KubeconfigJwt:PublicKeyPath` takes a single path today, so during rotation you add the new key as an additional `IssuerSigningKey` in the gateway's `JwtBearer` config before swapping the file.
3. Point the ControlPlane at the new private key via `KubeconfigJwt:PrivateKeyPath`. All newly issued kubeconfig JWTs are now signed with the new key.
4. Wait at least 8 hours (the maximum TTL of any outstanding kubeconfig JWT) plus a safety margin.
5. Remove the old public key from the gateway.

**Blast radius of a leaked kubeconfig private key.** An attacker can forge kubeconfig JWTs for any user and any cluster until you rotate. Response:

1. Rotate the keypair immediately using the procedure above, but do not wait — remove the old public key as soon as the new one is active.
2. Bulk-revoke all outstanding `AccessToken` records (set `RevokedAt` on every row where `Kind = Kubeconfig` and `RevokedAt IS NULL`). Every user re-runs `clustral kube login`.
3. Review audit logs (`category=Credential`, `code=CCR001I`) for any issuances you did not expect.

## Internal JWT

**What it proves.** The request came through the API Gateway after successful authentication under one of the gateway's JWT schemes. Downstream services (ControlPlane, AuditService) trust this token as the sole basis for treating the caller as authenticated.

**Claims.** `sub`, `email`, `name`, `preferred_username`. TTL is 30 seconds.

**Where it is validated.** Every authenticated request to the ControlPlane REST API and every request to the AuditService. Validated via `Clustral.Sdk.Auth.InternalJwtService.ForValidation(publicKeyPem)`. The header is `X-Internal-Token`.

**Issuance.** The `InternalJwtMiddleware` in the API Gateway signs a fresh token per request after OIDC or kubeconfig JWT validation succeeds. Tokens are not cached — 30 seconds is short enough that caching adds complexity without meaningful savings.

**What happens when it expires.** The downstream service returns 401. Because the TTL is 30 seconds and the token is minted synchronously per request, this should never happen in practice. If it does, the user's clock is significantly skewed from the gateway's.

**Rotation procedure.**

1. Generate a new ES256 keypair.
2. Roll out the new public key to every downstream service (ControlPlane, AuditService) first. Configure them to accept both the old and new public key.
3. Switch the API Gateway to the new private key.
4. Wait 30 seconds (the maximum lifetime of any outstanding internal JWT).
5. Remove the old public key from downstream services.

**Blast radius of a leaked internal JWT private key.** Anyone with the key can impersonate any gateway-authenticated user against downstream services for as long as the key is trusted. The 30-second TTL means forged tokens must be used within 30 seconds of signing, but the key itself remains dangerous until you rotate. Response:

1. Rotate the keypair immediately.
2. Audit every request that reached the ControlPlane or AuditService since the suspected compromise.
3. Review account lockout / password reset policies for any user whose identity might have been impersonated.

## mTLS — the agent trust anchor

The agent's authentication to the ControlPlane is X.509 mTLS, not a JWT. Clustral runs its own certificate authority (`infra/ca/`), generated at install time, and signs a per-agent client certificate during bootstrap.

**CA location.** `CertificateAuthority:CaCertPath` (public) and `CertificateAuthority:CaKeyPath` (private) on the ControlPlane. Defaults in production: `/etc/clustral/ca.crt` and `/etc/clustral/ca.key`.

**Agent bootstrap.** An admin creates a single-use bootstrap token (`POST /api/v1/clusters`) and hands it to the agent operator. The agent calls `ClusterService.RegisterAgent` with the token; on success the ControlPlane issues a per-agent client certificate and a JWT, and marks the bootstrap token consumed. The token cannot be reused — if the agent's cert is lost, the cluster needs a new bootstrap token.

**Certificate validity.** `CertificateAuthority:ClientCertValidityDays` — default 395 days. Chosen so certs outlive a typical operational calendar quarter without pushing past browser/tooling maximums.

**Renewal.** Agents call `ClusterService.RenewCredentials` ahead of expiry. Renewal succeeds as long as the cluster is still registered and the current cert has not been revoked. If renewal fails long enough for the cert to expire, the agent cannot reconnect and must be re-bootstrapped.

**Revocation.** Deregistering a cluster (`DELETE /api/v1/clusters/{id}`) removes the agent's record; subsequent tunnel attempts are refused because `TunnelServiceImpl.OpenTunnel` rejects agents whose cluster row is absent. Clustral does not publish a CRL — revocation is enforced by the ControlPlane's authorization check, not by the TLS handshake.

**Blast radius of a leaked CA private key.** An attacker can issue agent certificates that the ControlPlane will accept on the tunnel port. Every agent must be re-bootstrapped. Procedure:

1. Generate a new CA keypair and swap `CertificateAuthority:CaCertPath` and `CertificateAuthority:CaKeyPath` on the ControlPlane.
2. Restart the ControlPlane to load the new CA.
3. Every agent's current cert is now invalid. For each cluster: `DELETE /api/v1/clusters/{id}` to deregister, then `POST /api/v1/clusters` to re-register and receive a fresh single-use bootstrap token. Hand each token to the agent operator and re-deploy.
4. Audit every `ClusterRegisteredEvent` (`code=CCL001I`) and `AgentAuthFailedEvent` (`code=CAG001W`) since the suspected compromise.

## Key rotation calendar (recommended)

| Key | Frequency | Procedure |
|---|---|---|
| OIDC signing keys | Per your IdP policy | IdP-managed; gateway auto-picks up via JWKS |
| Kubeconfig JWT keypair | Annually | Two-key overlap, 8h drain |
| Internal JWT keypair | Annually | Two-key overlap, 30s drain |
| Clustral CA | Every 3–5 years | Re-issue CA, re-bootstrap every agent |
| Agent client cert | Automatic, ahead of expiry | `ClusterService.RenewCredentials` |

Rotate earlier than the table suggests if you have reason to suspect a key is compromised. The costs of rotation are bounded and scripted; the cost of continuing to trust a compromised key is not.

## Threat model

This section lists the threats Clustral's JWT and mTLS design is intended to contain, and the containment mechanism in each case.

**Stolen OIDC token on a laptop.** Limited by the IdP's token TTL (typically ≤1 hour) and scope. If the user signs out of the IdP, the token is a paperweight — the IdP refuses to refresh it. Response: the user signs out of the IdP; you optionally force-logout their session at the IdP.

**Stolen kubeconfig JWT.** Valid for up to the remaining TTL (max 8 hours). Revocation via `POST /api/v1/auth/revoke-by-token` is immediate for the proxy path. The attacker can use the token against the proxy from anywhere on the internet until you revoke.

**Stolen internal JWT.** Valid for at most 30 seconds. The window is short enough that exfiltration and reuse before expiry is hard. The internal JWT never leaves the internal network in normal operation, so a theft implies the attacker is already inside.

**Compromised agent host.** The attacker has the agent's cluster-level impersonation capability — they can act as any user whose role resolves to that cluster on any subsequent kubectl request routed through the agent. Response: deregister the cluster via the ControlPlane, rotate the agent's bootstrap token, and re-deploy the agent from a known-good image on a known-good host. The compromised cert is then useless.

**Compromised ControlPlane host.** Full system compromise. The attacker has the MongoDB credentials, every signing private key, and the CA private key. Standard defenses apply:

- Host hardening (minimal base image, no shell on the production container).
- Secrets in a proper secret store (HashiCorp Vault, cloud KMS, Kubernetes SealedSecrets) rather than on disk. Clustral supports reading key material from any file path, so mount it from your secret store.
- Immutable deploys — never SSH into production; re-deploy instead.
- Log aggregation off the host, so audit trails survive host compromise.
- Alert on any `CAG001W` (agent auth failed) and unexpected `CCR001I` (credential issued) events.

Recovery from a full ControlPlane compromise requires rotating every signing key, rotating the CA, re-bootstrapping every agent, and invalidating every outstanding kubeconfig JWT. Plan for it; do not assume it will never happen.

## See also

- [Authentication Flows](../architecture/authentication-flows.md) — how these credentials move between CLI, gateway, ControlPlane, and agent.
- [Audit Log](audit-log.md) — where credential issuance, revocation, and agent auth failures are recorded.
- [On-Prem Docker Compose](../getting-started/on-prem-docker-compose.md) — where the keypair files live in a production install.
- [Network Map](../architecture/network-map.md) — which ports carry which credential type.
