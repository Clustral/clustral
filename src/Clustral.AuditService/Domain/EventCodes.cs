namespace Clustral.AuditService.Domain;

/// <summary>
/// Catalog of all audit event codes. Format: <c>[PREFIX][NUMBER][SEVERITY]</c>.
/// Mirrors Teleport's event code convention.
///
/// Prefixes:
/// <list type="bullet">
///   <item><c>CAR</c> — Access Requests</item>
///   <item><c>CCR</c> — Credentials</item>
///   <item><c>CCL</c> — Clusters</item>
///   <item><c>CRL</c> — Roles</item>
///   <item><c>CUA</c> — User Auth</item>
///   <item><c>CPR</c> — Proxy</item>
/// </list>
///
/// Severity suffixes: <c>I</c> = Info, <c>W</c> = Warning, <c>E</c> = Error.
/// </summary>
public static class EventCodes
{
    // ── Access Requests ──────────────────────────────────────────────────
    public const string AccessRequestCreated  = "CAR001I";
    public const string AccessRequestApproved = "CAR002I";
    public const string AccessRequestDenied   = "CAR003W";
    public const string AccessRequestRevoked  = "CAR004I";
    public const string AccessRequestExpired  = "CAR005I";

    // ── Credentials ─────────────────────────────────────────────────────
    public const string CredentialIssued  = "CCR001I";
    public const string CredentialRevoked = "CCR002I";

    // ── Clusters ────────────────────────────────────────────────────────
    public const string ClusterRegistered   = "CCL001I";
    public const string ClusterConnected    = "CCL002I";
    public const string ClusterDisconnected = "CCL003W";
    public const string ClusterDeleted      = "CCL004I";

    // ── Roles ───────────────────────────────────────────────────────────
    public const string RoleCreated = "CRL001I";
    public const string RoleUpdated = "CRL002I";
    public const string RoleDeleted = "CRL003I";

    // ── User Auth ───────────────────────────────────────────────────────
    public const string UserSynced      = "CUA001I";
    public const string RoleAssigned    = "CUA002I";
    public const string RoleUnassigned  = "CUA003I";

    // ── Proxy ───────────────────────────────────────────────────────────
    public const string ProxyRequestCompleted = "CPR001I";
    public const string ProxyAccessDenied     = "CPR002W";

    /// <summary>All event codes for validation and uniqueness checks.</summary>
    public static readonly string[] All =
    [
        AccessRequestCreated, AccessRequestApproved, AccessRequestDenied,
        AccessRequestRevoked, AccessRequestExpired,
        CredentialIssued, CredentialRevoked,
        ClusterRegistered, ClusterConnected, ClusterDisconnected, ClusterDeleted,
        RoleCreated, RoleUpdated, RoleDeleted,
        UserSynced, RoleAssigned, RoleUnassigned,
        ProxyRequestCompleted, ProxyAccessDenied,
    ];
}
