using System.CommandLine.Invocation;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Http;  // CliHttp.IsTimeout
using Clustral.Cli.Ui;

namespace Clustral.Cli.Commands;

/// <summary>
/// Resolves human-friendly resource names (e.g. "prod") to their GUID IDs
/// by fetching the ControlPlane list endpoint and matching case-insensitively.
/// If the input already parses as a GUID, it is returned unchanged with no
/// HTTP call, so existing scripts that pass GUIDs see zero overhead.
/// </summary>
internal static class NameResolver
{
    /// <summary>
    /// Resolves a cluster name or GUID to a cluster ID.
    /// Returns the resolved ID, or <c>null</c> after writing an error via
    /// <see cref="CliErrors"/> and setting <c>ctx.ExitCode = 1</c>.
    /// </summary>
    public static async Task<string?> ResolveClusterIdAsync(
        HttpClient http,
        string nameOrId,
        InvocationContext ctx,
        CancellationToken ct)
    {
        CliDebug.Log($"Resolving cluster: input='{nameOrId}', isGuid={Guid.TryParse(nameOrId, out _)}");

        if (Guid.TryParse(nameOrId, out _))
            return nameOrId;

        ClusterListResponse? response;
        try
        {
            var json = await http.GetStringAsync("api/v1/clusters", ct);
            response = JsonSerializer.Deserialize(json, CliJsonContext.Default.ClusterListResponse);
        }
        catch (Exception ex) when (CliHttp.IsTimeout(ex))
        {
            CliErrors.WriteError(Messages.Errors.ClusterResolveTimeout);
            ctx.ExitCode = 1;
            return null;
        }
        catch (Exception ex)
        {
            CliErrors.WriteConnectionError(ex);
            ctx.ExitCode = 1;
            return null;
        }

        var matches = response?.Clusters
            .Where(c => c.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        if (matches.Count == 0)
        {
            CliErrors.WriteError(Messages.Errors.ClusterNotFound(nameOrId));
            ctx.ExitCode = 1;
            return null;
        }

        if (matches.Count > 1)
        {
            var ids = string.Join(", ", matches.Select(c => c.Id));
            CliErrors.WriteError(Messages.Errors.AmbiguousClusters(nameOrId, ids));
            ctx.ExitCode = 1;
            return null;
        }

        CliDebug.Log($"Resolved cluster '{nameOrId}' → {matches[0].Id} ({matches[0].Name})");
        return matches[0].Id;
    }

    /// <summary>
    /// Resolves a role name or GUID to a role ID.
    /// Returns the resolved ID, or <c>null</c> after writing an error via
    /// <see cref="CliErrors"/> and setting <c>ctx.ExitCode = 1</c>.
    /// </summary>
    public static async Task<string?> ResolveRoleIdAsync(
        HttpClient http,
        string nameOrId,
        InvocationContext ctx,
        CancellationToken ct)
    {
        CliDebug.Log($"Resolving role: input='{nameOrId}', isGuid={Guid.TryParse(nameOrId, out _)}");

        if (Guid.TryParse(nameOrId, out _))
            return nameOrId;

        RoleListResponse? response;
        try
        {
            var json = await http.GetStringAsync("api/v1/roles", ct);
            response = JsonSerializer.Deserialize(json, CliJsonContext.Default.RoleListResponse);
        }
        catch (Exception ex) when (CliHttp.IsTimeout(ex))
        {
            CliErrors.WriteError(Messages.Errors.RoleResolveTimeout);
            ctx.ExitCode = 1;
            return null;
        }
        catch (Exception ex)
        {
            CliErrors.WriteConnectionError(ex);
            ctx.ExitCode = 1;
            return null;
        }

        var matches = response?.Roles
            .Where(r => r.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        if (matches.Count == 0)
        {
            CliErrors.WriteError(Messages.Errors.RoleNotFound(nameOrId));
            ctx.ExitCode = 1;
            return null;
        }

        if (matches.Count > 1)
        {
            var ids = string.Join(", ", matches.Select(r => r.Id));
            CliErrors.WriteError(Messages.Errors.AmbiguousRoles(nameOrId, ids));
            ctx.ExitCode = 1;
            return null;
        }

        CliDebug.Log($"Resolved role '{nameOrId}' → {matches[0].Id} ({matches[0].Name})");
        return matches[0].Id;
    }
}
