using System.Net.Http.Headers;

namespace Clustral.Sdk.Grpc;

/// <summary>
/// Delegating handler that attaches a bearer token to every outgoing request.
/// Reads the token lazily from a <see cref="Func{T}"/> so that token rotation
/// (e.g. a silent refresh) is picked up without recreating the channel.
/// </summary>
internal sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly Func<CancellationToken, Task<string>> _tokenProvider;

    /// <param name="tokenProvider">
    /// Called on every request.  Should return quickly (e.g. from an
    /// in-memory cache) to avoid adding latency to every gRPC call.
    /// </param>
    /// <param name="innerHandler">The next handler in the pipeline.</param>
    internal BearerTokenHandler(
        Func<CancellationToken, Task<string>> tokenProvider,
        HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
