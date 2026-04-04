using Clustral.Agent.Tunnel;
using Clustral.Sdk.Grpc;
using Clustral.V1;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;

namespace Clustral.Agent.Worker;

/// <summary>
/// The primary <see cref="BackgroundService"/> for the Clustral Agent.
/// Responsible for the startup credential lifecycle and then handing off to
/// <see cref="TunnelManager"/> for the long-lived connection loop.
/// </summary>
public sealed class AgentWorker : BackgroundService
{
    private readonly AgentOptions         _opts;
    private readonly AgentCredentialStore _credentials;
    private readonly TunnelManager        _tunnel;
    private readonly ILogger<AgentWorker> _logger;

    public AgentWorker(
        IOptions<AgentOptions>  opts,
        AgentCredentialStore    credentials,
        TunnelManager           tunnel,
        ILogger<AgentWorker>    logger)
    {
        _opts        = opts.Value;
        _credentials = credentials;
        _tunnel      = tunnel;
        _logger      = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Clustral Agent starting — cluster {ClusterId}, control plane {Url}",
            _opts.ClusterId, _opts.ControlPlaneUrl);

        // ── Step 1: ensure we have a valid agent credential ───────────────────
        await EnsureCredentialAsync(stoppingToken);

        // ── Step 2: run the tunnel (reconnect loop) ───────────────────────────
        await _tunnel.RunAsync(stoppingToken);

        _logger.LogInformation("Clustral Agent stopped.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Credential lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private async Task EnsureCredentialAsync(CancellationToken ct)
    {
        // If a bootstrap token is provided, always issue a fresh credential.
        // This handles the case where a stale token file exists from a
        // previously deleted cluster.
        if (!string.IsNullOrWhiteSpace(_opts.BootstrapToken))
        {
            _logger.LogInformation("Bootstrap token provided — issuing fresh credential.");
            await IssueCredentialAsync(ct);
            return;
        }

        var stored = await _credentials.ReadTokenAsync(ct);

        if (stored is null)
        {
            _logger.LogInformation(
                "No agent credential found at {Path} — exchanging bootstrap token.",
                _credentials.TokenPath);

            await IssueCredentialAsync(ct);
            return;
        }

        var expiresAt = await _credentials.ReadExpiryAsync(ct);
        if (expiresAt.HasValue &&
            expiresAt.Value - DateTimeOffset.UtcNow < _opts.CredentialRotationThreshold)
        {
            _logger.LogInformation(
                "Agent credential expires in {Days:F0} days — rotating.",
                (expiresAt.Value - DateTimeOffset.UtcNow).TotalDays);

            await RotateCredentialAsync(stored, ct);
        }
    }

    private async Task IssueCredentialAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BootstrapToken))
            throw new InvalidOperationException(
                "Agent:BootstrapToken is required when no credential file exists. " +
                "Set it via the Agent__BootstrapToken environment variable.");

        if (string.IsNullOrWhiteSpace(_opts.AgentPublicKeyPem))
            _logger.LogWarning("Agent:AgentPublicKeyPem is not set — skipping public key verification. " +
                               "Set it in production for mutual identity verification.");

        // Use an unauthenticated channel (bootstrap token provided in the request body).
        using var channel = CreateUnauthenticatedChannel();
        var client        = new AuthService.AuthServiceClient(channel);

        _logger.LogInformation("Calling AuthService.IssueAgentCredential…");
        var response = await client.IssueAgentCredentialAsync(new IssueAgentCredentialRequest
        {
            ClusterId          = _opts.ClusterId,
            BootstrapToken     = _opts.BootstrapToken,
            AgentPublicKeyPem  = _opts.AgentPublicKeyPem,
        }, cancellationToken: ct);

        var expiresAt = response.ExpiresAt.ToDateTimeOffset();
        await _credentials.StoreAsync(response.Token, expiresAt, ct);

        _logger.LogInformation(
            "Agent credential issued (id={CredentialId}, expires={ExpiresAt:O})",
            response.CredentialId, expiresAt);
    }

    private async Task RotateCredentialAsync(string currentToken, CancellationToken ct)
    {
        using var channel = CreateUnauthenticatedChannel();
        var client        = new AuthService.AuthServiceClient(channel);

        var response = await client.RotateAgentCredentialAsync(new RotateAgentCredentialRequest
        {
            ClusterId    = _opts.ClusterId,
            CurrentToken = currentToken,
        }, cancellationToken: ct);

        var expiresAt = response.ExpiresAt.ToDateTimeOffset();
        await _credentials.StoreAsync(response.Token, expiresAt, ct);

        _logger.LogInformation(
            "Agent credential rotated (id={CredentialId}, expires={ExpiresAt:O})",
            response.CredentialId, expiresAt);
    }

    // For credential issuance/rotation the request is self-authenticating
    // (bootstrap token or current agent token in the request body), so we
    // don't need a bearer token on the channel.
    private GrpcChannel CreateUnauthenticatedChannel()
    {
        var url = _opts.ControlPlaneUrl;

        AppContext.SetSwitch(
            "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport",
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

        return GrpcChannel.ForAddress(url);
    }
}
