using System.Text;
using DotNet.Testcontainers.Containers;

namespace Clustral.E2E.Tests.Fixtures;

/// <summary>
/// Polls a container's stdout/stderr looking for a substring within a deadline.
/// Used by E2E tests to detect renewal events emitted by the Go agent
/// (e.g. "Certificate renewed successfully", "JWT renewed successfully").
/// </summary>
public static class AgentLogReader
{
    /// <summary>
    /// Reads the container logs in a polling loop and returns true once
    /// <paramref name="needle"/> appears, or false on timeout.
    /// </summary>
    public static async Task<bool> WaitForLogLineAsync(
        IContainer container,
        string needle,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var seen = new StringBuilder();

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var (stdout, stderr) = await container.GetLogsAsync(ct: ct);
                seen.Clear();
                seen.Append(stdout);
                seen.Append('\n');
                seen.Append(stderr);

                if (seen.ToString().Contains(needle, StringComparison.Ordinal))
                    return true;
            }
            catch
            {
                // Container might be transiently unavailable; keep polling.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }

        return false;
    }

    /// <summary>
    /// Returns all current log output (stdout + stderr) for diagnostic dumps
    /// in xUnit failures.
    /// </summary>
    public static async Task<string> DumpAsync(IContainer container, CancellationToken ct = default)
    {
        try
        {
            var (stdout, stderr) = await container.GetLogsAsync(ct: ct);
            return $"=== STDOUT ===\n{stdout}\n=== STDERR ===\n{stderr}";
        }
        catch (Exception ex)
        {
            return $"(failed to read container logs: {ex.Message})";
        }
    }
}
