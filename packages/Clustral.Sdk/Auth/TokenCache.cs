using System.Text;

namespace Clustral.Sdk.Auth;

/// <summary>
/// Persists the user's JWT to <c>~/.clustral/token</c> and reads it back.
/// Provides both in-process locking (SemaphoreSlim) and cross-process
/// exclusive file access (FileShare.None) so concurrent CLI invocations
/// do not corrupt the token file.
/// </summary>
public sealed class TokenCache
{
    // In-process guard: prevents two threads within the same process from
    // racing on file open/create.
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    private readonly string _tokenPath;

    // How many times to retry acquiring the OS-level file lock before giving up.
    private const int MaxRetries   = 8;
    private const int RetryDelayMs = 50;

    public TokenCache() : this(DefaultTokenPath()) { }

    public TokenCache(string tokenPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenPath);
        _tokenPath = tokenPath;
    }

    /// <summary>Returns <c>~/.clustral/token</c>.</summary>
    public static string DefaultTokenPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".clustral",
            "token");

    /// <summary>
    /// Writes <paramref name="token"/> to the token file, creating the
    /// parent directory if it does not exist.
    /// </summary>
    public async Task StoreAsync(string token, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        Directory.CreateDirectory(Path.GetDirectoryName(_tokenPath)!);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WithExclusiveFileAsync(
                _tokenPath,
                FileMode.Create,
                FileAccess.Write,
                async (stream, innerCt) =>
                {
                    await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
                    await writer.WriteAsync(token).ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Reads the token from the token file, or returns <c>null</c> if the
    /// file does not exist.
    /// </summary>
    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_tokenPath))
            return null;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? result = null;

            await WithExclusiveFileAsync(
                _tokenPath,
                FileMode.Open,
                FileAccess.Read,
                async (stream, innerCt) =>
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    var raw = await reader.ReadToEndAsync(innerCt).ConfigureAwait(false);
                    result = raw.Trim();
                    // Treat a file that contains only whitespace as absent.
                    if (string.IsNullOrEmpty(result))
                        result = null;
                },
                ct).ConfigureAwait(false);

            return result;
        }
        catch (FileNotFoundException)
        {
            // Deleted between Exists check and Open — treat as absent.
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Deletes the token file if it exists.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(_tokenPath))
                File.Delete(_tokenPath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens <paramref name="path"/> with <c>FileShare.None</c> and invokes
    /// <paramref name="action"/>.  Retries on <see cref="IOException"/> to
    /// handle brief cross-process contention.
    /// </summary>
    private static async Task WithExclusiveFileAsync(
        string path,
        FileMode mode,
        FileAccess access,
        Func<FileStream, CancellationToken, Task> action,
        CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var fs = new FileStream(
                    path,
                    mode,
                    access,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true);

                await action(fs, ct).ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs, ct).ConfigureAwait(false);
            }
        }
    }
}
