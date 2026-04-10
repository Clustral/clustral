namespace Clustral.Cli.Ui;

/// <summary>
/// Centralized catalog of user-facing CLI messages. Plain English text only —
/// Spectre.Console markup is applied by callers. Keeps wording consistent
/// across commands and enables future i18n by replacing this single file.
/// </summary>
internal static class Messages
{
    internal static class Errors
    {
        public const string Timeout = "ControlPlane unreachable (timed out after 5s).";
        public const string NotLoggedIn = "Not logged in";
        public const string ControlPlaneNotConfigured = "ControlPlane URL not configured";
        public const string EmptyToken =
            "ControlPlane returned an empty token. Your session may have expired — run 'clustral login' first.";
        public const string Cancelled = "Operation cancelled.";
        public const string CannotDetermineBinaryPath = "Could not determine binary path.";
        public const string ClusterResolveTimeout = "ControlPlane unreachable while resolving cluster name.";
        public const string RoleResolveTimeout = "ControlPlane unreachable while resolving role name.";

        public static string ClusterNotFound(string name) =>
            $"Cluster '{name}' not found. Run 'clustral clusters list' to see available clusters.";

        public static string RoleNotFound(string name) =>
            $"Role '{name}' not found. Run 'clustral roles list' to see available roles.";

        public static string AmbiguousClusters(string name, string ids) =>
            $"Multiple clusters named '{name}' found ({ids}). Use the cluster ID instead.";

        public static string AmbiguousRoles(string name, string ids) =>
            $"Multiple roles named '{name}' found ({ids}). Use the role ID instead.";

        public static string WriteFailed(string detail) =>
            $"Writing kubeconfig failed: {detail}";

        public static string NoBinary(string artifact, string tag) =>
            $"No binary found for {artifact} in release {tag}.";
    }

    internal static class Hints
    {
        public const string RunLogin = "clustral login";
        public const string RunLoginWithUrl = "clustral login <url>";
    }

    internal static class Success
    {
        public const string LoggedOutLocally = "Logged out locally.";
        public const string LoggedIn = "Logged in successfully";
        public const string KubeconfigUpdated = "Kubeconfig updated";
        public const string AccessRequestCreated = "Access request created";
        public const string AccessApproved = "Access approved!";
        public const string AccessDenied = "Access request denied.";
        public const string AccessRevoked = "Access grant revoked.";

        public const string Cleaned = "Cleaned. Run 'clustral login <url>' to set up again.";

        public static string CredentialsRevoked(int count) =>
            $"Revoked {count} credential(s) on ControlPlane.";
    }

    internal static class Warnings
    {
        public const string ControlPlaneUnreachable =
            "Could not reach ControlPlane — local logout complete.";
        public const string CredentialsExpireNaturally =
            "Server-side credentials will expire on their own.";
        public const string NoCredentialsRevoked =
            "No credentials revoked on ControlPlane.";
        public const string RevocationFailed =
            "ControlPlane revocation failed — local logout complete.";

        public static string ContextNotFound(string name) =>
            $"Context {name} not found in kubeconfig.";
    }

    internal static class Spinners
    {
        public const string LoadingClusters = "Loading clusters...";
        public const string LoadingUsers = "Loading users...";
        public const string LoadingRoles = "Loading roles...";
        public const string LoadingAccessRequests = "Loading access requests...";
        public const string IssuingCredential = "Issuing kubeconfig credential...";
        public const string CheckingUpdates = "Checking for updates...";
        public const string ApprovingRequest = "Approving access request...";
        public const string DenyingRequest = "Denying access request...";
        public const string RevokingGrant = "Revoking access grant...";
        public const string FetchingVersion = "Fetching ControlPlane version...";

        public static string RevokingCredentials(int count) =>
            $"Revoking {count} credential(s) on ControlPlane...";

        public static string ResolvingCluster(string name) =>
            $"Resolving cluster '{name}'...";

        public static string ResolvingRole(string name) =>
            $"Resolving role '{name}'...";

        public static string Downloading(string version, string artifact) =>
            $"Downloading v{version} for {artifact}...";

        public static string DiscoveringConfig(string url) =>
            $"Discovering ControlPlane configuration at {url}...";
    }
}
