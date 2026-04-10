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
            CliErrors.WriteError("ControlPlane unreachable while resolving cluster name.");
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
            CliErrors.WriteError(
                $"Cluster '{nameOrId}' not found. Run 'clustral clusters list' to see available clusters.");
            ctx.ExitCode = 1;
            return null;
        }

        if (matches.Count > 1)
        {
            var ids = string.Join(", ", matches.Select(c => c.Id));
            CliErrors.WriteError(
                $"Multiple clusters named '{nameOrId}' found ({ids}). Use the cluster ID instead.");
            ctx.ExitCode = 1;
            return null;
        }

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
            CliErrors.WriteError("ControlPlane unreachable while resolving role name.");
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
            CliErrors.WriteError(
                $"Role '{nameOrId}' not found. Run 'clustral roles list' to see available roles.");
            ctx.ExitCode = 1;
            return null;
        }

        if (matches.Count > 1)
        {
            var ids = string.Join(", ", matches.Select(r => r.Id));
            CliErrors.WriteError(
                $"Multiple roles named '{nameOrId}' found ({ids}). Use the role ID instead.");
            ctx.ExitCode = 1;
            return null;
        }

        return matches[0].Id;
    }
}
