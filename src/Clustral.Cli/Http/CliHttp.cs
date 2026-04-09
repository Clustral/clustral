using Spectre.Console;

namespace Clustral.Cli.Http;

/// <summary>
/// Shared HTTP client factory + spinner helper for the CLI.
///
/// Centralizes timeout, TLS skip-verify, and base URL configuration so every
/// command behaves consistently when the ControlPlane is unreachable. All
/// remote calls should go through <see cref="RunWithSpinnerAsync{T}"/> so the
/// user sees feedback during the wait and timeouts produce a recognisable
/// <see cref="CliHttpTimeoutException"/>.
/// </summary>
internal static class CliHttp
{
    /// <summary>Default per-request timeout (5 seconds).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Creates a new <see cref="HttpClient"/> with a base URL, optional
    /// TLS skip-verify, and a configurable timeout (default 5 s).
    /// </summary>
    public static HttpClient CreateClient(
        string baseUrl,
        bool insecureTls = false,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseUrl);

        var handler = new HttpClientHandler();
        if (insecureTls)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = timeout ?? DefaultTimeout,
        };
    }

    /// <summary>
    /// Runs an HTTP call inside a Spectre.Console spinner. The spinner shows
    /// while <paramref name="work"/> is running and clears when it returns.
    /// Timeout-related exceptions are converted to
    /// <see cref="CliHttpTimeoutException"/> so callers can detect them.
    /// </summary>
    public static async Task<T> RunWithSpinnerAsync<T>(
        string statusMessage,
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default)
    {
        T result = default!;
        Exception? captured = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync(statusMessage, async _ =>
            {
                try
                {
                    result = await work(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            }).ConfigureAwait(false);

        if (captured is not null)
        {
            if (IsTimeout(captured))
                throw new CliHttpTimeoutException(statusMessage, captured);
            throw captured;
        }
        return result;
    }

    /// <summary>Non-generic overload for void HTTP calls.</summary>
    public static async Task RunWithSpinnerAsync(
        string statusMessage,
        Func<CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        await RunWithSpinnerAsync<bool>(statusMessage, async c =>
        {
            await work(c).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns true if the exception was caused by an HTTP timeout
    /// (HttpClient.Timeout or a cancelled token from a timeout).
    /// </summary>
    public static bool IsTimeout(Exception ex)
    {
        return ex is TaskCanceledException
            || ex is OperationCanceledException
            || (ex.InnerException is TaskCanceledException)
            || (ex.InnerException is OperationCanceledException);
    }
}

/// <summary>
/// Thrown when an HTTP call wrapped in <see cref="CliHttp.RunWithSpinnerAsync{T}"/>
/// times out. Carries the spinner status message so callers can include it in
/// error output.
/// </summary>
internal sealed class CliHttpTimeoutException : Exception
{
    public string StatusMessage { get; }

    public CliHttpTimeoutException(string statusMessage, Exception? inner = null)
        : base($"Timed out: {statusMessage}", inner)
    {
        StatusMessage = statusMessage;
    }
}
