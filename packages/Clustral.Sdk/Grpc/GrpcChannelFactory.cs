using Clustral.Sdk.Auth;
using Grpc.Net.Client;

namespace Clustral.Sdk.Grpc;

/// <summary>
/// Creates <see cref="GrpcChannel"/> instances pre-configured with a bearer
/// token from <see cref="TokenCache"/> or a caller-supplied token.
/// </summary>
/// <remarks>
/// <para>
/// Channels are expensive to create and should be reused across calls.
/// Store the returned <see cref="GrpcChannel"/> for the lifetime of the
/// component that needs it, and dispose it on shutdown.
/// </para>
/// <para>
/// The <see cref="BearerTokenHandler"/> inside each channel re-reads the
/// token on every HTTP/2 request, so a token rotation (e.g. silent refresh
/// written by <see cref="TokenCache.StoreAsync"/>) is picked up without
/// recreating the channel.
/// </para>
/// </remarks>
public sealed class GrpcChannelFactory
{
    private readonly TokenCache _tokenCache;

    public GrpcChannelFactory(TokenCache tokenCache)
    {
        _tokenCache = tokenCache;
    }

    /// <summary>
    /// Creates a TLS-secured channel to <paramref name="address"/>, reading
    /// the bearer token from <see cref="TokenCache"/> on every call.
    /// </summary>
    /// <param name="address">
    /// HTTPS address of the ControlPlane gRPC endpoint
    /// (e.g. <c>https://cp.example.com:5001</c>).
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown immediately if no token is currently stored, so the caller gets
    /// a fast, clear error rather than a gRPC Unauthenticated status on the
    /// first RPC.
    /// </exception>
    public async Task<GrpcChannel> CreateAuthenticatedChannelAsync(
        string address,
        CancellationToken ct = default)
    {
        // Eager validation — fail fast before the channel is even created.
        var initial = await _tokenCache.ReadAsync(ct).ConfigureAwait(false);
        if (initial is null)
            throw new InvalidOperationException(
                "No Clustral token found. Run 'clustral login' first.");

        var handler = new BearerTokenHandler(
            tokenProvider: async innerCt =>
                await _tokenCache.ReadAsync(innerCt).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Token was removed while a gRPC call was in flight. Run 'clustral login' again."),
            innerHandler: new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                PooledConnectionIdleTimeout    = TimeSpan.FromMinutes(5),
                KeepAlivePingDelay             = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout           = TimeSpan.FromSeconds(30),
            });

        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler,
        });
    }

    /// <summary>
    /// Creates a TLS-secured channel with a caller-supplied token.
    /// Useful when the token is already in memory (e.g. just issued by
    /// <c>AuthService</c>) and storing it to disk first would be wasteful.
    /// </summary>
    public static GrpcChannel CreateWithToken(string address, string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        var handler = new BearerTokenHandler(
            tokenProvider: _ => Task.FromResult(token),
            innerHandler: new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
            });

        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler,
        });
    }

    /// <summary>
    /// Creates a plain-HTTP (no TLS) channel for local dev environments such
    /// as a kind cluster or docker-compose stack.
    /// </summary>
    /// <remarks>
    /// Calls <c>AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)</c>
    /// which is a process-wide setting.  Safe in CLI and Worker Service
    /// scenarios where mixed TLS/non-TLS channels are not required.
    /// </remarks>
    public static GrpcChannel CreateInsecureWithToken(string address, string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        // Required for HTTP/2 without TLS in .NET.
        AppContext.SetSwitch(
            "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var handler = new BearerTokenHandler(
            tokenProvider: _ => Task.FromResult(token),
            innerHandler: new SocketsHttpHandler());

        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler,
        });
    }
}
