# Changelog

All notable changes to Clustral.

## [0.1.0-alpha.2] — 2026-04-18

### Bug Fixes

- move EnsureIndexesAsync to ApplicationStarted so config validation runs first (43df96d)
- correct Release CLI badge filter and use static license badge (30edf77)
- agent discovers k8s version, removes version from heartbeat proto (842ba0c)
- mermaid parse error — replace ambiguous triple-dash link syntax (351c267)
- quote parentheses in mermaid link label to prevent parse error (05322a4)
- register test CA in WebApplicationFactory for gRPC integration tests (e53a92c)
- persist OIDC settings from discovery response during login (8ecba3a)
- use plain styling for Y/N confirmation prompts (57e4c09)
- use existing Prometheus v3.2.1 image tag (5a68d3a)
- use existing Tempo v2.6.1 image tag (fa78d7d)
- remove cross-file depends_on for audit-service (58fd0c6)
- add Dockerfile for AuditService (4a2e7e5)
- add Clustral.Contracts to ControlPlane Dockerfile (1c468ac)
- Prometheus /metrics endpoint + MongoDB Guid serialization (4e549cb)
- cluster connected/disconnected audit events + full event type test coverage (94f4bfb)
- Grafana logs, traces, and dashboard provisioning (d2607e1)
- add acting user identity to all audit events (bfb450c)
- add RabbitMQ Testcontainer to E2E test fixture (90c1af0)
- register MongoDB GuidSerializer in test fixture (17d5940)
- prevent path traversal in profile names and account emails (726b81b)
- adapt workflows for branch protection on main ([#21](https://github.com/Clustral/clustral/issues/21)) (a01d4a2)
- add CI Gate job + fix helm push lowercase org name ([#24](https://github.com/Clustral/clustral/issues/24)) (a3acf96)
- stamp chart version from git tag + build deps before release ([#26](https://github.com/Clustral/clustral/issues/26)) (06415e1)
- strip v prefix from VERSION build-arg in release images ([#27](https://github.com/Clustral/clustral/issues/27)) (37c5efe)

### CI/CD

- enterprise-grade CI/CD with changelog, pre-release channels, and test reporting (4e30345)

### Documentation

- add CLAUDE.md for Go agent (893894f)
- update Web CLAUDE.md for Next.js + shadcn/ui (3ac9531)
- complete CLAUDE.md updates across all sub-projects (be3f4a7)
- update README with full CLI commands and test counts (30b8917)
- require unit + integration tests for every new feature (e4b4dfd)
- update README and CLAUDE.md with vertical slicing architecture (5ac0a0a)
- add CI/CD, release, and tech stack badges to README (74a0a15)
- add enterprise network map with ports, protocols, and zones to README (447bbe5)
- improve network map diagram readability and colors (c9df88e)
- update README and CLAUDE.md with gRPC integration test coverage (ec348e7)
- update diagrams and docs for nginx unified gateway architecture (f2841ba)
- document E2E test suite in README and CLAUDE.md files (fad140d)
- add E2E test architecture diagrams to README (ab7245b)
- add audit command + page to CLAUDE.md (8ff406b)
- add audit logging section + update diagrams with event codes (1f11ab3)
- author all non-error sections (9 pages) + GitBook config ([#15](https://github.com/Clustral/clustral/issues/15)) (1ae111a)
- fix .gitbook.yaml — redirects must be a mapping, not null ([#16](https://github.com/Clustral/clustral/issues/16)) (2437d60)
- fix GitBook nav — 40 error pages + 14 missing CLI commands ([#17](https://github.com/Clustral/clustral/issues/17)) (ffe3b4f)
- add SECURITY.md — vulnerability reporting + hardening checklist (a83a8e2)

### Features

- adopt shadcn/ui for Web UI components (f0810f8)
- role editing + shadcn migration for roles page (f58f2e8)
- auto-logout on 401 Unauthorized API response (027d09e)
- colorized CLI with ANSI-aware column padding (969d792)
- Spectre.Console CLI, enterprise KubeconfigWriter, JIT access requests, kube logout, comprehensive tests (9d42b04)
- grant revocation, duration granularity, active grants listing (580c28d)
- CPM, Result pattern, global exception handler, integration tests, CLI errors, roles list, docs update (52a31c5)
- Go agent unit tests + CI cleanup (75ec2fc)
- vertical slicing architecture with MediatR, FluentValidation, FluentAssertions (a4665d9)
- config validation with FluentValidation, rename Keycloak → OIDC provider (7686828)
- enterprise-grade health checks with liveness, readiness, and detailed endpoints (63d82d2)
- FluentValidation for CLI input parameters (eead41e)
- CLI integration tests against real ControlPlane API (af62146)
- DDD Phase 1 — AccessRequest aggregate root, UserSyncService, domain tests (14bfa3d)
- DDD for Cluster, Role, RoleAssignment, and User domain objects (6121b66)
- DDD Phase 3 — AccessSpecifications for reusable query predicates (800c6a5)
- DDD Phase 4 — repository interfaces for all domain entities (e8c4cf7)
- DDD Phase 2 — domain events for all aggregates with audit logging (9b5ea71)
- enterprise-grade kubectl proxy with DDD, rate limiting, and structured logging (af6e2d7)
- CQS (Command-Query Separation) with ICommand/IQuery markers and folder structure (032cbf7)
- unified versioning across ControlPlane, CLI, and Agent (b974e8a)
- nginx as unified gateway, CLI discovers ControlPlane directly (a8c99fd)
- mTLS + JWT authentication for agent-ControlPlane gRPC tunnel (24152dd)
- mTLS tests, port migration, diagrams, and legacy removal (452d48d)
- clustral config — show CLI files, session, and kubeconfig state (d1211a6)
- CLI responsiveness — timeouts, spinners, and local-first logout (90628a2)
- render JWT "Valid until" inside login profile table (93eaf34)
- accept cluster/role names wherever the CLI takes a GUID (c2c0317)
- accept shorthand durations (8H, 30M, 1D) in --ttl and --duration (52af1ca)
- polish OIDC callback HTML pages with modern styling (dcd2f4f)
- --debug flag + global exception handler for the CLI (80ae9a7)
- add verbose step-by-step debug logging to every CLI command (f3bf517)
- add --output json|table global option for all list commands (97db6d7)
- add --no-color flag and NO_COLOR env var support (44c504d)
- add `clustral status` — session, clusters, grants, and health (02023dc)
- add `clustral doctor` — sequential connectivity diagnostics (69d7e87)
- shell completions (bash/zsh/fish) + fix doctor OIDC TLS timeout (58f9244)
- auto-login prompt when JWT session is expired or missing (a5d0268)
- configuration profiles for multi-environment switching (4d2458f)
- add Profile section to `clustral config show` (5255077)
- configuration profiles for multi-environment switching (ebdfa02)
- add spinner to clustral doctor during diagnostics (212b880)
- `clustral config clean` — factory reset the CLI (001c3e8)
- add `clustral whoami` — instant identity and session check (0c6175b)
- add debug logs to whoami/doctor/profile + mandate in CLAUDE.md (e45fc6d)
- multi-account login + rename profile → profiles (ccfc572)
- account integration across all CLI commands + OIDC prompt=login (3ac0f22)
- Phase 1 — Clustral.Contracts with 18 integration events (b31c8f5)
- Phase 2+3 — SDK Messaging + Telemetry extensions (4a9bc6e)
- Phase 4 — Wire MassTransit + OTEL into ControlPlane (eedd747)
- Phase 5 — AuditService scaffold with domain model (34ce2be)
- Phase 6 — MassTransit consumers for all 18 integration events (03946f7)
- Phase 7 — REST API for querying audit events (6bf76ce)
- Phase 8 — Infrastructure (RabbitMQ + Grafana stack) (398db00)
- Phase 9 — AuditService tests (63 test cases) (541ae09)
- Phase 10+11 — ControlPlane publish tests + SDK option tests (334560b)
- Phase 12 — Document event-driven architecture in CLAUDE.md (f984b7f)
- CLI `clustral audit` command with filters + table/JSON (4588879)
- Web UI types, API wrapper, proxy route, and TanStack hook (01b7465)
- Web UI audit log page with filters and pagination (b9bfc62)
- enrich events with user/cluster context, CQS proxy refactor, detail view (d671e69)
- add CPR002W proxy.access_denied audit event (2b59f1b)
- add warning events for agent auth, credential, and proxy failures (c943866)
- add YARP API Gateway with centralized OIDC + internal JWT auth ([#10](https://github.com/Clustral/clustral/issues/10)) (9ec586c)
- unify error response shapes (v1.Status on proxy, RFC 7807 elsewhere) ([#12](https://github.com/Clustral/clustral/issues/12)) (bc0b156)
- docs site skeleton + auto-generated error catalog ([#14](https://github.com/Clustral/clustral/issues/14)) (cd3015e)
- Helm charts for agent + platform with OCI registry CI ([#18](https://github.com/Clustral/clustral/issues/18)) (38683a3)
- switch from GHCR OCI to GitHub Pages via chart-releaser ([#25](https://github.com/Clustral/clustral/issues/25)) (e02d7f1)

### Miscellaneous

- use bun throughout Web Dockerfile (237d207)
- remove depends_on between web and controlplane (cddefe1)

### Refactoring

- move generated proto stubs to gen/clustral/v1/ (b020a28)
- use local module name for Go agent (53898d8)
- move controllers into Api/Controllers/V1/ version folder (5a597f8)
- remove dead AgentPublicKeyPem field (f772d6a)
- flatten CliErrors from cards to indicator + dim-detail layout (a1e2bb9)
- centralize CLI user-facing strings into Messages.cs (6c1491e)
- Extract CQS to SDK, AuditService uses MediatR vertical slicing (1168317)
- Apply DDD to AuditService (8f96363)
- rename GetByUidAsync → GetByIdAsync + add naming conventions (92c57ea)
- remove all observability/OTEL infrastructure (eb37ce7)
- fire-and-forget for proxy access denied audit event (6d73344)
- extract docker-compose variables to .env files + update docs (83a4ea9)

### Testing

- gRPC integration tests for ClusterService and AuthService (e801112)
- end-to-end test suite with K3s, Keycloak, and real Go agent (fb76c6c)
- add 12 E2E tests + fix E2E fixture for slnx, mongo auth, k3s SA (cd89dae)

## [0.1.0-alpha.1] — 2026-04-05

### Bug Fixes

- include bun.lock for reproducible Docker builds (6b1b308)
- keep OIDC client config in .env, fetch only authority at runtime (3525ed4)
- proxy Keycloak through nginx to avoid CORS/mixed-content (075dbe9)
- remove missing public/ dir from Web Dockerfile (ba261b3)
- replace custom server.ts with CommonJS HTTPS wrapper for standalone (e0a97f0)
- simplify Web Dockerfile following Vercel's Next.js Docker example (51e048d)
- replace build-time rewrites with runtime API route proxies (18ac453)
- remove HOSTNAME env var from Web Dockerfile (4cadf7e)
- use /api/proxy/ path for kubectl tunnel so Next.js routes it (3004f9d)
- proxy route uses /api/proxy/ path and reads env at request time (b13bfae)
- agent HttpClient double-send and concurrent stream writes (ee83c15)
- use ghcr.io agent image in k8s deployment (8d5436b)
- ensure Authorization header is forwarded in proxy route (603fe15)
- use static token in kubeconfig, fix JwtBearer issuer validation (cba7e02)
- use require('next') instead of internal path in HTTPS server (09a6bb5)
- remove HTTPS from Next.js, back to plain Vercel Docker pattern (a064b3d)
- restore plain server URL in kube login, remove kube proxy (7cb8915)
- strip gzip headers from proxy, auto insecure-skip-tls for HTTPS (11dc1ad)
- add metrics.k8s.io RBAC, suppress JWT noise on gRPC (6fba4fa)
- split impersonation RBAC rules to avoid Rancher CRD conflicts (0a10479)
- use wildcard apiGroups for impersonation to bypass CRD conflicts (389aa26)
- use IEnumerable overload for Impersonate-Group headers (7e8e9d6)
- use raw HTTP handler to send separate Impersonate-Group headers (c72446c)
- WireFormatTest now documents .NET behavior instead of failing (470bcaf)
- use SocketsHttpHandler ConnectCallback for header rewriting (f495276)
- load in-cluster CA cert for k8s API TLS in ImpersonationHandler (04e005c)
- rewrite ImpersonationHandler as full raw HTTP client (321e7ec)
- use Go 1.25 in Dockerfile and CI to match go.mod (6ba7738)
- use macos-latest for both macOS builds in release-cli (ac17c3f)

### CI/CD

- only run pipelines when relevant files change (cc0ce22)
- trigger pipeline rebuild (58ed6da)
- add workflow_dispatch to both pipelines for manual trigger (87d0a2a)

### Documentation

- update README with on-prem deployment guide and current state (ec08c67)
- update README for HTTPS, single-origin proxy architecture (19d35fd)
- update README for Next.js migration and multi-provider OIDC (da72fa8)
- update README docker-compose with host IP pattern (d21c3e2)
- add comprehensive "How It Works" section to README (72a792b)
- update README for Go agent, access management, mermaid diagrams (fd8a88a)
- update README with install section, project diagrams, release flow (b90d5be)

### Features

- runtime OIDC discovery for Web UI (3fff509)
- HTTPS with self-signed certs for Web UI (7afa42d)
- migrate Web UI from Vite+React SPA to Next.js 14 (7b35f3c)
- HTTPS for kubectl — self-signed cert on Web UI + local proxy command (79eca9e)
- standalone nginx SSL proxy for HTTPS termination (897d69a)
- wire Serilog for ControlPlane and Agent (ee14548)
- add `clustral clusters list` command (3282ea7)
- add `clustral kube ls` — list clusters like `tsh kube ls` (9a21859)
- show cluster ID in kube ls output (4c33175)
- add `clustral logout` — revoke credentials and clean up (5e4c9d9)
- implement k8s user impersonation for per-user RBAC (275aa63)
- per-cluster role-based access, separate infra, display names (eab87b0)
- rewrite Agent in Go — eliminates .NET header workaround (4277a56)
- Teleport-style login profile display (32dcd1f)
- skip re-auth if session is valid, add --force flag (4d53f5c)
- CLI release pipeline, self-update, install scripts, Homebrew tap (36bd618)

### Miscellaneous

- remove debug impersonation logging (b1dd628)

### Other

- add impersonation logging to ControlPlane proxy and Agent (a5b9abd)

### Testing

- add ImpersonationHandler tests proving separate headers (8234387)


