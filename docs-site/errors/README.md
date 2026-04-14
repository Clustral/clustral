# Error Reference

Every error response from the Clustral platform includes a machine-readable
error code in the `X-Clustral-Error-Code` header (and in the RFC 7807 `type`
URL for REST API responses). Click any code below for details, causes, and
remediation steps.

| Code | HTTP | Kind | Category | Summary |
|---|---|---|---|---|
| [`AGENT_ERROR`](agent-error.md) | 500 | Internal | Auth & Proxy | The Clustral agent reported an error while proxying to the Kubernetes API (<p... |
| [`AGENT_NOT_CONNECTED`](agent-not-connected.md) | 500 | Internal | Auth & Proxy | The Clustral agent for cluster 00000000-0000-0000-0000-000000000000 is not cu... |
| [`AUTHENTICATION_REQUIRED`](authentication-required.md) | 401 | Unauthorized | Auth & Proxy | Authentication required: this request is missing the 'Authorization: Bearer <... |
| [`BAD_REQUEST`](bad-request.md) | 400 | BadRequest | Exception Handler | The request is malformed or contains invalid arguments. |
| [`CLIENT_CLOSED`](client-closed.md) | 499 | Internal | Exception Handler | The client closed the connection before the server could respond. |
| [`CLUSTER_MISMATCH`](cluster-mismatch.md) | 403 | Forbidden | Cluster & Role | This kubeconfig credential was issued for cluster 00000000-0000-0000-0000-000... |
| [`CLUSTER_NOT_FOUND`](cluster-not-found.md) | 404 | NotFound | Cluster & Role | Cluster '<placeholder>' not found. |
| [`CREDENTIAL_EXPIRED`](credential-expired.md) | 401 | Unauthorized | User & Credential | Your kubeconfig credential (00000000-0000-0000-0000-000000000000) expired at ... |
| [`CREDENTIAL_NOT_FOUND`](credential-not-found.md) | 404 | NotFound | User & Credential | Credential not found. |
| [`CREDENTIAL_REVOKED`](credential-revoked.md) | 401 | Unauthorized | User & Credential | Your kubeconfig credential (00000000-0000-0000-0000-000000000000) has been re... |
| [`DUPLICATE_CLUSTER_NAME`](duplicate-cluster-name.md) | 409 | Conflict | Cluster & Role | Cluster named '<placeholder>' already exists. |
| [`DUPLICATE_ROLE_NAME`](duplicate-role-name.md) | 409 | Conflict | Cluster & Role | Role named '<placeholder>' already exists. |
| [`FORBIDDEN`](forbidden.md) | 403 | Forbidden | Auth & Proxy | You can only revoke your own credentials. |
| [`GATEWAY_ERROR`](gateway-error.md) | 500 | Internal | Gateway | An unexpected error occurred in the API Gateway. |
| [`GRANT_ALREADY_ACTIVE`](grant-already-active.md) | 409 | Conflict | Access Request | You already have an active JIT grant for this cluster. |
| [`GRANT_ALREADY_EXPIRED`](grant-already-expired.md) | 409 | Conflict | Access Request | Grant has already expired. |
| [`GRANT_ALREADY_REVOKED`](grant-already-revoked.md) | 409 | Conflict | Access Request | Grant has already been revoked. |
| [`GRANT_NOT_APPROVED`](grant-not-approved.md) | 409 | Conflict | Access Request | Only approved grants can be revoked. Current status: <placeholder>. |
| [`INVALID_CLUSTER_ID`](invalid-cluster-id.md) | 400 | BadRequest | Other | The cluster ID '<placeholder>' in the proxy URL is not a valid UUID. Your kub... |
| [`INVALID_DURATION`](invalid-duration.md) | 400 | BadRequest | Access Request | Invalid ISO 8601 duration: '<placeholder>'. |
| [`INVALID_FORMAT`](invalid-format.md) | 400 | BadRequest | Validation & Generic | <placeholder> |
| [`INVALID_TOKEN`](invalid-token.md) | 401 | Unauthorized | Auth & Proxy | Your bearer token has expired. Run 'clustral kube login <cluster>' to obtain ... |
| [`NO_ROLE_ASSIGNMENT`](no-role-assignment.md) | 403 | Forbidden | Auth & Proxy | <placeholder> has no active role on cluster 00000000-0000-0000-0000-000000000... |
| [`PENDING_REQUEST_EXISTS`](pending-request-exists.md) | 409 | Conflict | Access Request | You already have a pending request for this cluster. |
| [`RATE_LIMITED`](rate-limited.md) | 429 | Forbidden | Gateway | Too many requests. Slow down and retry after the period indicated in the Retr... |
| [`REQUEST_EXPIRED`](request-expired.md) | 409 | Conflict | Access Request | Request has expired. |
| [`REQUEST_NOT_PENDING`](request-not-pending.md) | 409 | Conflict | Access Request | Request is already <placeholder>. |
| [`ROLE_NOT_FOUND`](role-not-found.md) | 404 | NotFound | Cluster & Role | Role '<placeholder>' not found. |
| [`ROUTE_NOT_FOUND`](route-not-found.md) | 404 | NotFound | Gateway | No route matches the requested path. Check the URL and try again. |
| [`STATIC_ASSIGNMENT_EXISTS`](static-assignment-exists.md) | 409 | Conflict | Access Request | You already have a static role assignment for this cluster. |
| [`TIMEOUT`](timeout.md) | 504 | Internal | Exception Handler | The operation timed out before completing. |
| [`TUNNEL_ERROR`](tunnel-error.md) | 500 | Internal | Auth & Proxy | An internal error occurred while forwarding the request through the agent tun... |
| [`TUNNEL_TIMEOUT`](tunnel-timeout.md) | 500 | Internal | Auth & Proxy | The Clustral agent did not respond within 00:02:00. The Kubernetes API server... |
| [`UNAUTHORIZED`](unauthorized.md) | 401 | Unauthorized | User & Credential | Authentication required. |
| [`UNPROCESSABLE`](unprocessable.md) | 422 | BadRequest | Exception Handler | The request is syntactically valid but semantically incorrect. |
| [`UPSTREAM_TIMEOUT`](upstream-timeout.md) | 504 | Internal | Gateway | The upstream service did not respond within the configured timeout. |
| [`UPSTREAM_UNAVAILABLE`](upstream-unavailable.md) | 503 | Internal | Gateway | The upstream service is temporarily unavailable (e.g. during a rolling restart). |
| [`UPSTREAM_UNREACHABLE`](upstream-unreachable.md) | 502 | Internal | Gateway | The upstream service (ControlPlane or AuditService) is not reachable. It may ... |
| [`USER_NOT_FOUND`](user-not-found.md) | 404 | NotFound | User & Credential | User not found. |
| [`VALIDATION_ERROR`](validation-error.md) | 422 | Validation | Validation & Generic | '<placeholder>' is required. |
