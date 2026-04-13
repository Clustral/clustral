namespace Clustral.Sdk.Results;

/// <summary>
/// Domain-specific error catalog. Provides consistent, machine-readable
/// error codes across the entire Clustral platform.
/// </summary>
public static class ResultErrors
{
    // ── Cluster ──────────────────────────────────────────────────────────

    public static ResultError ClusterNotFound(string id) =>
        ResultError.NotFound("CLUSTER_NOT_FOUND", $"Cluster '{id}' not found.");

    public static ResultError DuplicateClusterName(string name) =>
        ResultError.Conflict("DUPLICATE_CLUSTER_NAME", $"Cluster named '{name}' already exists.");

    // ── Role ─────────────────────────────────────────────────────────────

    public static ResultError RoleNotFound(string id) =>
        ResultError.NotFound("ROLE_NOT_FOUND", $"Role '{id}' not found.");

    public static ResultError DuplicateRoleName(string name) =>
        ResultError.Conflict("DUPLICATE_ROLE_NAME", $"Role named '{name}' already exists.");

    // ── User ─────────────────────────────────────────────────────────────

    public static ResultError UserNotFound() =>
        ResultError.NotFound("USER_NOT_FOUND", "User not found.");

    public static ResultError UserUnauthorized() =>
        ResultError.Unauthorized("Authentication required.");

    // ── Credential ───────────────────────────────────────────────────────

    public static ResultError CredentialNotFound() =>
        ResultError.NotFound("CREDENTIAL_NOT_FOUND", "Credential not found.");

    public static ResultError CredentialOwnerMismatch() =>
        ResultError.Forbidden("You can only revoke your own credentials.");

    public static ResultError InvalidCredential() =>
        ResultError.Unauthorized("Invalid or expired credential.");

    // ── Proxy / Auth (kubectl path) ───────────────────────────────────────
    // These factories are the canonical error codes surfaced on the
    // /api/proxy/* path. Keep in sync with the "Error Response Shapes" table
    // in the root README so integrators can switch on `code` programmatically.

    public static ResultError AuthenticationRequired() =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "AUTHENTICATION_REQUIRED",
            Message = "Authentication required — provide a valid kubeconfig credential.",
        };

    public static ResultError MalformedToken(string detail) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message = $"Token rejected: {detail}.",
        };

    public static ResultError MissingSubjectClaim() =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message = "Token rejected: missing sub claim.",
        };

    public static ResultError ClusterMismatch(Guid tokenCluster, Guid requestedCluster) =>
        new()
        {
            Kind = ResultErrorKind.Forbidden,
            Code = "CLUSTER_MISMATCH",
            Message = $"Credential issued for cluster {tokenCluster}, request is for cluster {requestedCluster}.",
        };

    public static ResultError CredentialRevoked(Guid credentialId) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "CREDENTIAL_REVOKED",
            Message = $"Credential {credentialId} has been revoked.",
        };

    public static ResultError CredentialExpired(Guid credentialId, DateTimeOffset expiredAt) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "CREDENTIAL_EXPIRED",
            Message = $"Credential {credentialId} expired at {expiredAt:O}.",
        };

    public static ResultError NoRoleAssignment(string identifier, string clusterName) =>
        new()
        {
            Kind = ResultErrorKind.Forbidden,
            Code = "NO_ROLE_ASSIGNMENT",
            Message = $"{identifier} has no active role on cluster {clusterName}. " +
                      "Request access with 'clustral access request'.",
        };

    public static ResultError InvalidClusterId(string raw) =>
        ResultError.BadRequest("INVALID_CLUSTER_ID",
            $"Cluster ID '{raw}' is not a valid UUID.", "clusterId");

    public static ResultError AgentNotConnected(Guid clusterId) =>
        new()
        {
            Kind = ResultErrorKind.Internal,
            Code = "AGENT_NOT_CONNECTED",
            Message = $"Agent for cluster {clusterId} is not connected.",
        };

    public static ResultError TunnelTimeout(TimeSpan timeout) =>
        new()
        {
            Kind = ResultErrorKind.Internal,
            Code = "TUNNEL_TIMEOUT",
            Message = $"Agent did not respond within {timeout}.",
        };

    public static ResultError TunnelError(string detail) =>
        new()
        {
            Kind = ResultErrorKind.Internal,
            Code = "TUNNEL_ERROR",
            Message = $"Tunnel proxy error: {detail}.",
        };

    public static ResultError AgentError(string agentCode, string detail) =>
        new()
        {
            Kind = ResultErrorKind.Internal,
            Code = "AGENT_ERROR",
            Message = $"Agent error ({agentCode}): {detail}.",
        };

    // ── Access Request ───────────────────────────────────────────────────

    public static ResultError StaticAssignmentExists() =>
        ResultError.Conflict("STATIC_ASSIGNMENT_EXISTS",
            "You already have a static role assignment for this cluster.");

    public static ResultError PendingRequestExists(Guid requestId) =>
        ResultError.Conflict("PENDING_REQUEST_EXISTS",
            "You already have a pending request for this cluster.",
            metadata: new Dictionary<string, object> { ["requestId"] = requestId });

    public static ResultError GrantAlreadyActive(Guid requestId) =>
        ResultError.Conflict("GRANT_ALREADY_ACTIVE",
            "You already have an active JIT grant for this cluster.",
            metadata: new Dictionary<string, object> { ["requestId"] = requestId });

    public static ResultError RequestNotPending(string currentStatus) =>
        ResultError.Conflict("REQUEST_NOT_PENDING",
            $"Request is already {currentStatus}.");

    public static ResultError RequestExpired() =>
        ResultError.Conflict("REQUEST_EXPIRED", "Request has expired.");

    public static ResultError GrantNotApproved(string currentStatus) =>
        ResultError.Conflict("GRANT_NOT_APPROVED",
            $"Only approved grants can be revoked. Current status: {currentStatus}.");

    public static ResultError GrantAlreadyRevoked() =>
        ResultError.Conflict("GRANT_ALREADY_REVOKED", "Grant has already been revoked.");

    public static ResultError GrantAlreadyExpired() =>
        ResultError.Conflict("GRANT_ALREADY_EXPIRED", "Grant has already expired.");

    public static ResultError InvalidDuration(string value) =>
        ResultError.BadRequest("INVALID_DURATION",
            $"Invalid ISO 8601 duration: '{value}'.", "requestedDuration");

    // ── Validation ───────────────────────────────────────────────────────

    public static ResultError RequiredField(string field) =>
        ResultError.Validation($"'{field}' is required.", field);

    public static ResultError InvalidFormat(string field, string detail) =>
        ResultError.BadRequest("INVALID_FORMAT", detail, field);
}
