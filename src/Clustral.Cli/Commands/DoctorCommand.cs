using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Kubeconfig;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral doctor</c>: sequential connectivity diagnostics
/// that check each layer (config, DNS, TLS, ControlPlane, OIDC, JWT,
/// kubeconfig) and report pass/fail with timing.
/// </summary>
internal static class DoctorCommand
{
    private static readonly Option<bool> InsecureOption = new(
        "--insecure", "Skip TLS verification.");

    public static Command Build()
    {
        var cmd = new Command("doctor", "Diagnose connectivity and configuration issues.");
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct       = ctx.GetCancellationToken();
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption);
        CliDebug.Log($"Running doctor checks (insecure: {insecure})");

        DoctorOutput checks = null!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("The doctor is in... checking your setup", async _ =>
            {
                checks = await RunChecksAsync(insecure, ct);
            });

        if (CliOptions.IsJson)
        {
            var json = JsonSerializer.Serialize(checks, CliJsonContext.Default.DoctorOutput);
            Console.WriteLine(json);
            return;
        }

        Render(AnsiConsole.Console, checks);

        var passed = checks.Checks.Count(c => c.Status == "pass");
        var failed = checks.Checks.Count(c => c.Status == "fail");
        var warned = checks.Checks.Count(c => c.Status == "warn");
        CliDebug.Log($"Doctor complete: {passed} passed, {warned} warnings, {failed} failed");
        if (failed > 0)
            ctx.ExitCode = 1;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Check runner — testable, returns a plain DTO.
    // ─────────────────────────────────────────────────────────────────────────

    internal static async Task<DoctorOutput> RunChecksAsync(bool insecure, CancellationToken ct)
    {
        var output = new DoctorOutput();

        // ── 1. Config ────────────────────────────────────────────────────
        var config = CliConfig.Load();
        if (string.IsNullOrWhiteSpace(config.ControlPlaneUrl))
        {
            output.Checks.Add(new DoctorCheck
            {
                Name = "Configuration",
                Status = "fail",
                Detail = "ControlPlane URL not configured. Run: clustral login <url>",
            });
            return output; // can't proceed without a URL
        }

        output.Checks.Add(new DoctorCheck
        {
            Name = "Configuration",
            Status = "pass",
            Detail = $"ControlPlane URL: {config.ControlPlaneUrl}",
        });
        insecure = insecure || config.InsecureTls;

        // Parse hostname from URL for DNS check.
        Uri uri;
        try { uri = new Uri(config.ControlPlaneUrl); }
        catch
        {
            output.Checks.Add(new DoctorCheck
            {
                Name = "URL parsing",
                Status = "fail",
                Detail = $"Invalid URL: {config.ControlPlaneUrl}",
            });
            return output;
        }

        // ── 2. DNS resolution ────────────────────────────────────────────
        var dnsCheck = await RunTimedCheckAsync("DNS resolution", async () =>
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            return $"{uri.Host} → {string.Join(", ", addresses.Select(a => a.ToString()))}";
        });
        output.Checks.Add(dnsCheck);
        if (dnsCheck.Status == "fail") return output;

        // ── 3. TLS handshake ─────────────────────────────────────────────
        if (uri.Scheme == "https" && !insecure)
        {
            var tlsCheck = await RunTimedCheckAsync("TLS handshake", async () =>
            {
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
                    {
                        if (cert is not null)
                        {
                            var notAfter = cert.NotAfter;
                            // Store cert info for the detail string.
                            return errors == SslPolicyErrors.None;
                        }
                        return false;
                    },
                };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                var response = await http.GetAsync(uri, ct);
                return $"Valid certificate, status {(int)response.StatusCode}";
            });
            output.Checks.Add(tlsCheck);
            if (tlsCheck.Status == "fail") return output;
        }
        else
        {
            output.Checks.Add(new DoctorCheck
            {
                Name = "TLS handshake",
                Status = "skip",
                Detail = insecure ? "Skipped (--insecure)" : "Skipped (HTTP, not HTTPS)",
            });
        }

        // ── 4. ControlPlane health ───────────────────────────────────────
        var cpCheck = await RunTimedCheckAsync("ControlPlane health", async () =>
        {
            using var http = CliHttp.CreateClient(config.ControlPlaneUrl, insecure);
            var json = await http.GetStringAsync("api/v1/config", ct);
            var cpConfig = JsonSerializer.Deserialize(json, CliJsonContext.Default.ControlPlaneConfig);
            return $"v{cpConfig?.Version}, 200 OK";
        });
        output.Checks.Add(cpCheck);

        // ── 5. OIDC discovery ────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(config.OidcAuthority))
        {
            var oidcCheck = await RunTimedCheckAsync("OIDC discovery", async () =>
            {
                var oidcHandler = new HttpClientHandler();
                if (insecure)
                    oidcHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                using var http = new HttpClient(oidcHandler) { Timeout = TimeSpan.FromSeconds(5) };
                var discoveryUrl = config.OidcAuthority.TrimEnd('/') + "/.well-known/openid-configuration";
                var response = await http.GetAsync(discoveryUrl, ct);
                response.EnsureSuccessStatusCode();
                return $"{config.OidcAuthority} (200 OK)";
            });
            output.Checks.Add(oidcCheck);
        }
        else
        {
            output.Checks.Add(new DoctorCheck
            {
                Name = "OIDC discovery",
                Status = "skip",
                Detail = "OIDC authority not configured",
            });
        }

        // ── 6. JWT session ───────────────────────────────────────────────
        var tokenPath = CliConfig.DefaultTokenPath;
        if (File.Exists(tokenPath))
        {
            try
            {
                var token = File.ReadAllText(tokenPath).Trim();
                var expiry = LoginCommand.DecodeJwtExpiry(token);
                if (expiry.HasValue)
                {
                    var remaining = expiry.Value - DateTimeOffset.UtcNow;
                    if (remaining > TimeSpan.Zero)
                    {
                        var validFor = remaining.TotalHours >= 1
                            ? $"{(int)remaining.TotalHours}h{remaining.Minutes}m"
                            : $"{(int)remaining.TotalMinutes}m";
                        output.Checks.Add(new DoctorCheck
                        {
                            Name = "JWT session",
                            Status = "pass",
                            Detail = $"Valid, expires {expiry.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} [{validFor} remaining]",
                        });
                    }
                    else
                    {
                        output.Checks.Add(new DoctorCheck
                        {
                            Name = "JWT session",
                            Status = "warn",
                            Detail = $"Expired at {expiry.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}. Run: clustral login",
                        });
                    }
                }
                else
                {
                    output.Checks.Add(new DoctorCheck
                    {
                        Name = "JWT session",
                        Status = "warn",
                        Detail = "Token exists but expiry could not be decoded",
                    });
                }
            }
            catch
            {
                output.Checks.Add(new DoctorCheck
                {
                    Name = "JWT session",
                    Status = "fail",
                    Detail = "Token file unreadable",
                });
            }
        }
        else
        {
            output.Checks.Add(new DoctorCheck
            {
                Name = "JWT session",
                Status = "warn",
                Detail = "Not logged in. Run: clustral login",
            });
        }

        // ── 7. Kubeconfig ────────────────────────────────────────────────
        var kubeconfigPath = KubeconfigWriter.DefaultKubeconfigPath();
        var contexts = LogoutCommand.FindClustralContexts(kubeconfigPath);
        if (contexts.Count > 0)
        {
            var withToken = contexts.Count(c => !string.IsNullOrEmpty(c.Token));
            output.Checks.Add(new DoctorCheck
            {
                Name = "Kubeconfig",
                Status = "pass",
                Detail = $"{contexts.Count} clustral context(s), {withToken} with active credentials",
            });
        }
        else
        {
            output.Checks.Add(new DoctorCheck
            {
                Name = "Kubeconfig",
                Status = "skip",
                Detail = "No clustral contexts. Run: clustral kube login <cluster>",
            });
        }

        return output;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rendering
    // ─────────────────────────────────────────────────────────────────────────

    internal static void Render(IAnsiConsole console, DoctorOutput data)
    {
        console.WriteLine();

        foreach (var check in data.Checks)
        {
            var (indicator, color) = check.Status switch
            {
                "pass" => ("✓", "green"),
                "warn" => ("!", "yellow"),
                "skip" => ("–", "grey"),
                _      => ("✗", "red"),
            };

            var timing = check.ElapsedMs.HasValue ? $" [dim]({check.ElapsedMs}ms)[/]" : "";
            console.MarkupLine($"  [{color}]{indicator}[/] {check.Name.EscapeMarkup()}{timing}");

            if (!string.IsNullOrEmpty(check.Detail))
                console.MarkupLine($"    [dim]{check.Detail.EscapeMarkup()}[/]");
        }

        console.WriteLine();

        var passed = data.Checks.Count(c => c.Status == "pass");
        var warned = data.Checks.Count(c => c.Status == "warn");
        var failed = data.Checks.Count(c => c.Status == "fail");

        if (failed == 0 && warned == 0)
            console.MarkupLine("[green]All checks passed.[/]");
        else if (failed == 0)
            console.MarkupLine($"[yellow]{warned} warning(s), {passed} passed.[/]");
        else
            console.MarkupLine($"[red]{failed} failed[/], {warned} warning(s), {passed} passed.");

        console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<DoctorCheck> RunTimedCheckAsync(
        string name, Func<Task<string>> check)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var detail = await check();
            sw.Stop();
            return new DoctorCheck
            {
                Name = name,
                Status = "pass",
                Detail = detail,
                ElapsedMs = sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DoctorCheck
            {
                Name = name,
                Status = "fail",
                Detail = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds,
            };
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class DoctorOutput
{
    [JsonPropertyName("checks")] public List<DoctorCheck> Checks { get; set; } = [];
}

internal sealed class DoctorCheck
{
    [JsonPropertyName("name")]      public string  Name      { get; set; } = string.Empty;
    [JsonPropertyName("status")]    public string  Status    { get; set; } = string.Empty;
    [JsonPropertyName("detail")]    public string? Detail    { get; set; }
    [JsonPropertyName("elapsedMs")] public long?   ElapsedMs { get; set; }
}
