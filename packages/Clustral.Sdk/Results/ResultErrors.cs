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

    // ── Auth & Proxy ──────────────────────────────────────────────────────
    // Factories below are the single source of truth for every
    // authentication / authorization / proxy error message surfaced from
    // the gateway and ControlPlane. Messages are deliberately self-speaking:
    // each one (1) names what went wrong, (2) names the component involved,
    // and (3) tells the user how to fix it. kubectl renders the Message
    // verbatim as "error: <message>", so this is what the end user sees in
    // their terminal. Keep in sync with the "Error Response Shapes" table
    // in the root README.
    //
    // The LoginHint parameter picks the remediation step: Kubectl →
    // "Run 'clustral kube login <cluster>' to obtain a fresh kubeconfig
    // credential."; Api → "Run 'clustral login' to sign in again.".

    /// <summary>
    /// How the user obtains a valid credential after the current one is
    /// rejected. Controls the last sentence of auth-error messages so each
    /// surface points the user at the right command.
    /// </summary>
    public enum LoginHint
    {
        /// <summary>Proxy path — user runs <c>clustral kube login &lt;cluster&gt;</c>.</summary>
        Kubectl,
        /// <summary>REST API path — user runs <c>clustral login</c>.</summary>
        Api,
    }

    private static string HintFor(LoginHint hint) => hint switch
    {
        LoginHint.Kubectl => "Run 'clustral kube login <cluster>' to obtain a fresh kubeconfig credential.",
        LoginHint.Api     => "Run 'clustral login' to sign in again.",
        _ => string.Empty,
    };

    public static ResultError AuthenticationRequired(LoginHint hint = LoginHint.Kubectl) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "AUTHENTICATION_REQUIRED",
            Message =
                "Authentication required: this request is missing the 'Authorization: Bearer <token>' header. " +
                HintFor(hint),
        };

    public static ResultError TokenExpired(LoginHint hint = LoginHint.Kubectl) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message = $"Your bearer token has expired. {HintFor(hint)}",
        };

    public static ResultError TokenInvalidSignature(LoginHint hint = LoginHint.Kubectl) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message =
                "Bearer token signature verification failed — this token was not issued by this Clustral " +
                $"ControlPlane (or by its configured OIDC provider). {HintFor(hint)}",
        };

    public static ResultError TokenInvalidIssuer(LoginHint hint = LoginHint.Kubectl) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message =
                "Bearer token issuer is not trusted — the token was issued by an OIDC provider this gateway does not recognize. " +
                HintFor(hint),
        };

    public static ResultError TokenInvalidAudience(LoginHint hint = LoginHint.Kubectl) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message =
                "Bearer token audience does not match this service — the token was issued for a different API. " +
                HintFor(hint),
        };

    public static ResultError TokenNotYetValid(LoginHint hint = LoginHint.Kubectl) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message =
                "Bearer token is not yet valid (its 'nbf' claim is in the future). Check your system clock — if it's correct, " +
                HintFor(hint).ToLowerInvariant(),
        };

    public static ResultError TokenMissingExpiration(LoginHint hint = LoginHint.Kubectl) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message =
                "Bearer token is missing the required 'exp' (expiration) claim — the credential is malformed. " +
                HintFor(hint),
        };

    public static ResultError TokenValidationFailed(string detail, LoginHint hint = LoginHint.Kubectl) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message = $"Bearer token was rejected during validation ({detail}). {HintFor(hint)}",
            Metadata = new Dictionary<string, object> { ["detail"] = detail },
        };

    public static ResultError AuthorizationFailed(LoginHint hint = LoginHint.Kubectl) =>
        new()
        {
            Kind = ResultErrorKind.Forbidden,
            Code = "FORBIDDEN",
            Message = hint switch
            {
                LoginHint.Kubectl =>
                    "Your bearer token is valid but is not authorized to access this cluster through the proxy. " +
                    "Ask an administrator to grant you a role on the cluster, or request just-in-time access with " +
                    "'clustral access request --cluster <cluster> --role <role-name>'.",
                _ =>
                    "Your bearer token is valid but is not authorized to access this resource. " +
                    "Ask an administrator to grant you the appropriate role.",
            },
        };

    public static ResultError MalformedToken(string detail) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message =
                $"Bearer token is malformed and could not be parsed ({detail}). " +
                "Run 'clustral kube login <cluster>' to obtain a fresh kubeconfig credential.",
        };

    public static ResultError MissingSubjectClaim() =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "INVALID_TOKEN",
            Message =
                "Bearer token is missing the required 'sub' (subject) claim. " +
                "The credential is corrupt or was issued by an older ControlPlane — " +
                "run 'clustral kube login <cluster>' to obtain a fresh one.",
        };

    public static ResultError ClusterMismatch(Guid tokenCluster, Guid requestedCluster) =>
        new()
        {
            Kind = ResultErrorKind.Forbidden,
            Code = "CLUSTER_MISMATCH",
            Message =
                $"This kubeconfig credential was issued for cluster {tokenCluster} " +
                $"but the request is for cluster {requestedCluster}. " +
                $"Run 'clustral kube login {requestedCluster}' to switch credentials.",
            Metadata = new Dictionary<string, object>
            {
                ["tokenClusterId"] = tokenCluster,
                ["requestedClusterId"] = requestedCluster,
            },
        };

    public static ResultError CredentialRevoked(Guid credentialId) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "CREDENTIAL_REVOKED",
            Message =
                $"Your kubeconfig credential ({credentialId}) has been revoked. " +
                "Run 'clustral kube login <cluster>' to obtain a new one.",
            Metadata = new Dictionary<string, object>
            {
                ["credentialId"] = credentialId,
            },
        };

    public static ResultError CredentialExpired(Guid credentialId, DateTimeOffset expiredAt) =>
        new()
        {
            Kind = ResultErrorKind.Unauthorized,
            Code = "CREDENTIAL_EXPIRED",
            Message =
                $"Your kubeconfig credential ({credentialId}) expired at {expiredAt:O}. " +
                "Run 'clustral kube login <cluster>' to refresh it.",
            Metadata = new Dictionary<string, object>
            {
                ["credentialId"] = credentialId,
                ["expiredAt"] = expiredAt,
            },
        };

    public static ResultError NoRoleAssignment(string identifier, Guid clusterId) =>
        new()
        {
            Kind = ResultErrorKind.Forbidden,
            Code = "NO_ROLE_ASSIGNMENT",
            Message =
                $"{identifier} has no active role on cluster {clusterId}. " +
                "Either ask an administrator to grant you a static role, or request just-in-time access " +
                $"with 'clustral access request --cluster {clusterId} --role <role-name>' " +
                "(run 'clustral clusters list' to map the cluster ID to its name).",
            Metadata = new Dictionary<string, object>
            {
                ["user"] = identifier,
                ["clusterId"] = clusterId,
            },
        };

    public static ResultError InvalidClusterId(string raw) =>
        new()
        {
            Kind = ResultErrorKind.BadRequest,
            Code = "INVALID_CLUSTER_ID",
            Message =
                $"The cluster ID '{raw}' in the proxy URL is not a valid UUID. " +
                "Your kubeconfig may be corrupt — re-run 'clustral kube login <cluster>' to regenerate it.",
            Field = "clusterId",
            Metadata = new Dictionary<string, object>
            {
                ["rawClusterId"] = raw,
            },
        };

    public static ResultError AgentNotConnected(Guid clusterId) =>
        new()
        {
            Kind = ResultErrorKind.Internal,
            Code = "AGENT_NOT_CONNECTED",
            Message =
                $"The Clustral agent for cluster {clusterId} is not currently connected to the ControlPlane. " +
                "The cluster may be offline or the agent deployment may be unhealthy. " +
                "Check 'clustral clusters list' for the cluster's status, and verify the agent pod is running in-cluster.",
            Metadata = new Dictionary<string, object>
            {
                ["clusterId"] = clusterId,
            },
        };

    public static ResultError TunnelTimeout(TimeSpan timeout) =>
        new()
        {
            Kind = ResultErrorKind.Internal,
            Code = "TUNNEL_TIMEOUT",
            Message =
                $"The Clustral agent did not respond within {timeout}. " +
                "The Kubernetes API server may be slow or the agent's network connectivity may be degraded. " +
                "Try again shortly; if the problem persists, check the agent pod logs.",
            Metadata = new Dictionary<string, object>
            {
                ["timeout"] = timeout,
            },
        };

    public static ResultError TunnelError(string detail) =>
        new()
        {
            Kind = ResultErrorKind.Internal,
            Code = "TUNNEL_ERROR",
            Message =
                $"An internal error occurred while forwarding the request through the agent tunnel: {detail}. " +
                "This is typically transient — retry the command.",
            Metadata = new Dictionary<string, object>
            {
                ["detail"] = detail,
            },
        };

    public static ResultError AgentError(string agentCode, string detail) =>
        new()
        {
            Kind = ResultErrorKind.Internal,
            Code = "AGENT_ERROR",
            Message =
                $"The Clustral agent reported an error while proxying to the Kubernetes API ({agentCode}): {detail}. " +
                "This usually means the agent cannot reach the Kubernetes API server — check the agent pod's network access.",
            Metadata = new Dictionary<string, object>
            {
                ["agentCode"] = agentCode,
                ["detail"] = detail,
            },
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
