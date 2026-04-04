using Microsoft.Extensions.Options;

namespace Clustral.Agent;

/// <summary>
/// Persists the agent's long-lived bearer token to disk so it survives
/// pod restarts without needing to contact the ControlPlane on every boot.
/// </summary>
/// <remarks>
/// Two files are written side by side:
/// <list type="bullet">
///   <item><c>{CredentialPath}</c> — the raw bearer token</item>
///   <item><c>{CredentialPath}.expiry</c> — the ISO-8601 expiry timestamp</item>
/// </list>
/// In k8s these files live on a <c>Secret</c>-backed volume or an
/// <c>emptyDir</c> volume.  The secret approach is preferred because it
/// persists across pod restarts.
/// </remarks>
public sealed class AgentCredentialStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string TokenPath  { get; }
    public string ExpiryPath { get; }

    public AgentCredentialStore(IOptions<AgentOptions> opts)
    {
        // Expand leading ~ so the dev path works.
        var raw = opts.Value.CredentialPath;
        var expanded = raw.StartsWith('~')
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                raw[2..])
            : raw;

        TokenPath  = expanded;
        ExpiryPath = expanded + ".expiry";
    }

    /// <summary>
    /// Returns the stored bearer token, or <c>null</c> if no credential file exists.
    /// </summary>
    public async Task<string?> ReadTokenAsync(CancellationToken ct = default)
    {
        if (!File.Exists(TokenPath)) return null;

        await _lock.WaitAsync(ct);
        try
        {
            var content = (await File.ReadAllTextAsync(TokenPath, ct)).Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Returns the stored expiry timestamp, or <c>null</c> if no expiry file exists.
    /// </summary>
    public async Task<DateTimeOffset?> ReadExpiryAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ExpiryPath)) return null;

        await _lock.WaitAsync(ct);
        try
        {
            var text = (await File.ReadAllTextAsync(ExpiryPath, ct)).Trim();
            return DateTimeOffset.TryParse(text, out var dt) ? dt : null;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Persists a new credential to disk, overwriting any existing credential.
    /// </summary>
    public async Task StoreAsync(
        string         token,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(TokenPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await _lock.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(TokenPath,  token, ct);
            await File.WriteAllTextAsync(ExpiryPath, expiresAt.ToString("O"), ct);
        }
        finally { _lock.Release(); }
    }
}
