using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Clustral.Cli.Config;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Kubeconfig;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral kube proxy</c>: starts a local HTTPS reverse proxy
/// that forwards kubectl traffic to the ControlPlane tunnel endpoint.
///
/// kubectl refuses to send bearer tokens over plain HTTP, so this proxy
/// provides an HTTPS endpoint on 127.0.0.1 with a self-signed certificate.
/// The kubeconfig is written with insecure-skip-tls-verify: true.
///
/// Similar to Teleport's <c>tsh proxy kube</c>.
/// </summary>
internal static class KubeProxyCommand
{
    private static readonly Argument<string> ClusterIdArg = new(
        "cluster-id",
        "ID of the cluster to proxy.");

    private static readonly Option<int> PortOption = new(
        "--port",
        () => 0,
        "Local port for the HTTPS proxy (0 = auto-select).");

    public static Command Build()
    {
        var cmd = new Command("proxy",
            "Start a local HTTPS proxy for kubectl. " +
            "Keeps running until Ctrl+C.");

        cmd.AddArgument(ClusterIdArg);
        cmd.AddOption(PortOption);

        cmd.SetHandler(HandleAsync);

        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct        = ctx.GetCancellationToken();
        var config    = CliConfig.Load();
        var clusterId = ctx.ParseResult.GetValueForArgument(ClusterIdArg);
        var port      = ctx.ParseResult.GetValueForOption(PortOption);

        var controlPlaneUrl = config.ControlPlaneUrl;
        if (string.IsNullOrWhiteSpace(controlPlaneUrl))
        {
            await Console.Error.WriteLineAsync(
                "error: ControlPlaneUrl is not set. Run 'clustral login <url>' first.");
            ctx.ExitCode = 1;
            return;
        }

        // Read the kubeconfig credential token.
        var cache = new TokenCache();
        var jwt = await cache.ReadAsync(ct);
        if (jwt is null)
        {
            await Console.Error.WriteLineAsync("error: No token found. Run 'clustral login' first.");
            ctx.ExitCode = 1;
            return;
        }

        // Issue a kubeconfig credential.
        IssueCredentialResponse credential;
        try
        {
            credential = await KubeLoginCommand.IssueCredentialAsync(
                controlPlaneUrl, jwt, clusterId, null, config.InsecureTls, ct);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            ctx.ExitCode = 1;
            return;
        }

        if (string.IsNullOrEmpty(credential.Token))
        {
            await Console.Error.WriteLineAsync(
                "error: Empty token. Session may have expired — run 'clustral login' first.");
            ctx.ExitCode = 1;
            return;
        }

        // Generate a self-signed cert for the local proxy.
        using var cert = GenerateSelfSignedCert();

        // Find a free port if 0 was specified.
        if (port == 0)
        {
            using var temp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            temp.Start();
            port = ((IPEndPoint)temp.LocalEndpoint).Port;
            temp.Stop();
        }

        var proxyUrl = $"https://127.0.0.1:{port}";
        var targetBase = $"{controlPlaneUrl.TrimEnd('/')}/api/proxy/{clusterId}";

        // Write kubeconfig pointing to the local proxy.
        var contextName = $"clustral-{clusterId}";
        var entry = new ClustralKubeconfigEntry(
            ContextName:           contextName,
            ServerUrl:             proxyUrl,
            Token:                 credential.Token,
            ExpiresAt:             credential.ExpiresAt,
            InsecureSkipTlsVerify: true);

        var writer = new KubeconfigWriter();
        writer.WriteClusterEntry(entry, setCurrentContext: true);

        Console.WriteLine($"Local HTTPS proxy starting on {proxyUrl}");
        Console.WriteLine($"  Forwarding to: {targetBase}");
        Console.WriteLine($"  Context: {contextName}");
        Console.WriteLine($"  Press Ctrl+C to stop.");
        Console.WriteLine();

        // Start the HTTPS reverse proxy.
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        })
        { Timeout = TimeSpan.FromMinutes(10) };

        using var listener = new HttpListener();
        listener.Prefixes.Add($"https://127.0.0.1:{port}/");

        // Bind the self-signed cert to the port (not needed on macOS/Linux with HttpListener workaround).
        // HttpListener on .NET doesn't support HTTPS directly on non-Windows.
        // Use Kestrel-like approach instead — fall back to a raw TcpListener + SslStream.
        await RunSslProxy(port, cert, targetBase, credential.Token, httpClient, ct);
    }

    private static async Task RunSslProxy(
        int port,
        X509Certificate2 cert,
        string targetBase,
        string bearerToken,
        HttpClient httpClient,
        CancellationToken ct)
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
        listener.Start();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, cert, targetBase, bearerToken, httpClient, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task HandleClientAsync(
        System.Net.Sockets.TcpClient client,
        X509Certificate2 cert,
        string targetBase,
        string bearerToken,
        HttpClient httpClient,
        CancellationToken ct)
    {
        try
        {
            using var _ = client;
            using var sslStream = new SslStream(client.GetStream(), false);
            await sslStream.AuthenticateAsServerAsync(cert, false, false);

            // Read the HTTP request line and headers from the SSL stream.
            using var reader = new StreamReader(sslStream, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(ct);
            if (requestLine is null) return;

            var parts = requestLine.Split(' ', 3);
            if (parts.Length < 2) return;

            var method = parts[0];
            var path   = parts[1];

            // Read headers.
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && line.Length > 0)
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                    headers[line[..colonIdx].Trim()] = line[(colonIdx + 1)..].Trim();
            }

            // Read body if Content-Length is present.
            byte[]? body = null;
            if (headers.TryGetValue("Content-Length", out var clStr) &&
                int.TryParse(clStr, out var contentLength) && contentLength > 0)
            {
                var buf = new char[contentLength];
                var read = await reader.ReadBlockAsync(buf, 0, contentLength);
                body = System.Text.Encoding.UTF8.GetBytes(buf, 0, read);
            }

            // Build the upstream request.
            var targetUrl = $"{targetBase}{path}";
            using var request = new HttpRequestMessage(new HttpMethod(method), targetUrl);

            // Forward headers, inject Bearer token.
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");
            foreach (var (name, value) in headers)
            {
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    continue;

                request.Headers.TryAddWithoutValidation(name, value);
            }

            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                if (headers.TryGetValue("Content-Type", out var ct2))
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", ct2);
            }

            // Send upstream.
            using var response = await httpClient.SendAsync(request, ct);

            // Write HTTP response back to the SSL stream.
            var statusLine = $"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n";
            var responseBody = await response.Content.ReadAsByteArrayAsync(ct);

            using var writer2 = new StreamWriter(sslStream, leaveOpen: true) { AutoFlush = false };
            await writer2.WriteAsync(statusLine);

            foreach (var (name, values) in response.Headers)
                await writer2.WriteAsync($"{name}: {string.Join(", ", values)}\r\n");
            foreach (var (name, values) in response.Content.Headers)
                await writer2.WriteAsync($"{name}: {string.Join(", ", values)}\r\n");

            await writer2.WriteAsync($"Content-Length: {responseBody.Length}\r\n");
            await writer2.WriteAsync("\r\n");
            await writer2.FlushAsync(ct);

            if (responseBody.Length > 0)
                await sslStream.WriteAsync(responseBody, ct);

            await sslStream.FlushAsync(ct);
        }
        catch (Exception)
        {
            // Client disconnect or timeout — ignore.
        }
    }

    private static X509Certificate2 GenerateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=clustral-proxy", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(
            new SubjectAlternativeNameBuilder().Apply(b => b.AddIpAddress(IPAddress.Loopback))
            ?? throw new InvalidOperationException());

        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(365));

        // On macOS/Linux, export/import as PFX so the private key is usable with SslStream.
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }
}

internal static class SanBuilderExtensions
{
    public static X509Extension? Apply(this SubjectAlternativeNameBuilder builder, Action<SubjectAlternativeNameBuilder> configure)
    {
        configure(builder);
        return builder.Build();
    }
}
