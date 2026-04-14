using System.Reflection;
using System.Text;
using Clustral.Sdk.Results;

namespace Clustral.Tools.DocsGen;

/// <summary>
/// Generates the error-catalog Markdown files for the docs site.
///
/// Discovers every error code from two sources:
///   1. Reflection on <see cref="ResultErrors"/> — invokes each factory with
///      placeholder args to capture <c>Code</c>, <c>Kind</c>, <c>Message</c>.
///   2. A hardcoded list of gateway / exception-handler codes that live in
///      <c>Program.cs</c> and <c>GlobalExceptionHandlerMiddleware</c> rather
///      than in the SDK (they aren't <see cref="ResultError"/> factories).
///
/// Output: one Markdown file per unique code in <c>docs-site/errors/</c>,
/// plus an index page at <c>docs-site/errors/README.md</c>.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var outputDir = Path.Combine(repoRoot, "docs-site", "errors");
        Directory.CreateDirectory(outputDir);

        var verify = args.Contains("--verify");

        // ── Discover error codes ────────────────────────────────────────────
        var errors = DiscoverFromResultErrors()
            .Concat(GatewayAndMiddlewareCodes())
            .GroupBy(e => e.Code)
            .Select(g => g.First() with { EmittedBy = g.SelectMany(e => e.EmittedBy).Distinct().ToList() })
            .OrderBy(e => e.Code)
            .ToList();

        Console.WriteLine($"Discovered {errors.Count} unique error codes.");

        // ── Generate files ──────────────────────────────────────────────────
        var generated = new List<string>();

        foreach (var error in errors)
        {
            var kebab = error.Code.ToLowerInvariant().Replace('_', '-');
            var filePath = Path.Combine(outputDir, $"{kebab}.md");
            var content = RenderErrorPage(error);

            if (File.Exists(filePath))
            {
                // Preserve hand-authored sections. The auto-generated parts are
                // between <!-- AUTO-GEN-START --> and <!-- AUTO-GEN-END --> markers.
                // If the file has no markers, it was written by a previous version;
                // overwrite it but preserve any "## What this means" content.
                var existing = File.ReadAllText(filePath);
                content = MergeWithExisting(existing, content, error);
            }

            File.WriteAllText(filePath, content);
            generated.Add(kebab);
        }

        // ── Generate index ──────────────────────────────────────────────────
        var indexPath = Path.Combine(outputDir, "README.md");
        File.WriteAllText(indexPath, RenderIndex(errors));
        Console.WriteLine($"Wrote index: {indexPath}");

        // ── Verify mode (CI) ────────────────────────────────────────────────
        if (verify)
        {
            // Check that no stale files exist (codes removed from source).
            var existingFiles = Directory.GetFiles(outputDir, "*.md")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(f => f != "README")
                .ToHashSet();

            var expected = generated.ToHashSet();
            var stale = existingFiles.Except(expected).ToList();

            if (stale.Count > 0)
            {
                Console.Error.WriteLine("ERROR: stale error-catalog pages detected:");
                foreach (var s in stale)
                    Console.Error.WriteLine($"  docs-site/errors/{s}.md");
                Console.Error.WriteLine("These codes no longer exist in the source. Delete the files and re-run.");
                return 1;
            }

            Console.WriteLine("Verify passed — no drift detected.");
        }

        Console.WriteLine($"Generated {generated.Count} error pages + index.");
        return 0;
    }

    // ─── Discovery from ResultErrors reflection ─────────────────────────────

    private static List<ErrorEntry> DiscoverFromResultErrors()
    {
        var results = new List<ErrorEntry>();
        var type = typeof(ResultErrors);

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != typeof(ResultError)) continue;

            var error = InvokeWithPlaceholders(method);
            if (error is null) continue;

            results.Add(new ErrorEntry
            {
                Code = error.Code,
                Kind = error.Kind,
                HttpStatus = MapKindToStatus(error.Kind),
                MessageTemplate = error.Message,
                EmittedBy = new List<string> { $"ResultErrors.{method.Name}" },
                Category = InferCategory(method.Name, error.Code),
            });
        }

        return results;
    }

    private static ResultError? InvokeWithPlaceholders(MethodInfo method)
    {
        try
        {
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.HasDefaultValue)
                {
                    args[i] = p.DefaultValue;
                }
                else if (p.ParameterType == typeof(string))
                {
                    args[i] = "<placeholder>";
                }
                else if (p.ParameterType == typeof(Guid))
                {
                    args[i] = Guid.Empty;
                }
                else if (p.ParameterType == typeof(DateTimeOffset))
                {
                    args[i] = DateTimeOffset.UtcNow;
                }
                else if (p.ParameterType == typeof(TimeSpan))
                {
                    args[i] = TimeSpan.FromMinutes(2);
                }
                else if (p.ParameterType.IsEnum)
                {
                    args[i] = Enum.GetValues(p.ParameterType).GetValue(0);
                }
                else
                {
                    args[i] = null;
                }
            }

            return method.Invoke(null, args) as ResultError;
        }
        catch
        {
            return null;
        }
    }

    // ─── Gateway / middleware codes (not in ResultErrors) ────────────────────

    private static List<ErrorEntry> GatewayAndMiddlewareCodes() =>
    [
        new()
        {
            Code = "RATE_LIMITED",
            Kind = ResultErrorKind.Forbidden,
            HttpStatus = 429,
            MessageTemplate = "Too many requests. Slow down and retry after the period indicated in the Retry-After header.",
            EmittedBy = ["ApiGateway rate-limiter"],
            Category = "Gateway",
        },
        new()
        {
            Code = "ROUTE_NOT_FOUND",
            Kind = ResultErrorKind.NotFound,
            HttpStatus = 404,
            MessageTemplate = "No route matches the requested path. Check the URL and try again.",
            EmittedBy = ["ApiGateway status-code handler"],
            Category = "Gateway",
        },
        new()
        {
            Code = "UPSTREAM_UNREACHABLE",
            Kind = ResultErrorKind.Internal,
            HttpStatus = 502,
            MessageTemplate = "The upstream service (ControlPlane or AuditService) is not reachable. It may be starting up or has crashed.",
            EmittedBy = ["ApiGateway status-code handler"],
            Category = "Gateway",
        },
        new()
        {
            Code = "UPSTREAM_UNAVAILABLE",
            Kind = ResultErrorKind.Internal,
            HttpStatus = 503,
            MessageTemplate = "The upstream service is temporarily unavailable (e.g. during a rolling restart).",
            EmittedBy = ["ApiGateway status-code handler"],
            Category = "Gateway",
        },
        new()
        {
            Code = "UPSTREAM_TIMEOUT",
            Kind = ResultErrorKind.Internal,
            HttpStatus = 504,
            MessageTemplate = "The upstream service did not respond within the configured timeout.",
            EmittedBy = ["ApiGateway status-code handler"],
            Category = "Gateway",
        },
        new()
        {
            Code = "GATEWAY_ERROR",
            Kind = ResultErrorKind.Internal,
            HttpStatus = 500,
            MessageTemplate = "An unexpected error occurred in the API Gateway.",
            EmittedBy = ["ApiGateway status-code handler"],
            Category = "Gateway",
        },
        new()
        {
            Code = "CLIENT_CLOSED",
            Kind = ResultErrorKind.Internal,
            HttpStatus = 499,
            MessageTemplate = "The client closed the connection before the server could respond.",
            EmittedBy = ["GlobalExceptionHandlerMiddleware"],
            Category = "Exception Handler",
        },
        new()
        {
            Code = "TIMEOUT",
            Kind = ResultErrorKind.Internal,
            HttpStatus = 504,
            MessageTemplate = "The operation timed out before completing.",
            EmittedBy = ["GlobalExceptionHandlerMiddleware"],
            Category = "Exception Handler",
        },
        new()
        {
            Code = "BAD_REQUEST",
            Kind = ResultErrorKind.BadRequest,
            HttpStatus = 400,
            MessageTemplate = "The request is malformed or contains invalid arguments.",
            EmittedBy = ["GlobalExceptionHandlerMiddleware"],
            Category = "Exception Handler",
        },
        new()
        {
            Code = "UNPROCESSABLE",
            Kind = ResultErrorKind.BadRequest,
            HttpStatus = 422,
            MessageTemplate = "The request is syntactically valid but semantically incorrect.",
            EmittedBy = ["GlobalExceptionHandlerMiddleware"],
            Category = "Exception Handler",
        },
    ];

    // ─── Rendering ──────────────────────────────────────────────────────────

    private static string RenderErrorPage(ErrorEntry error)
    {
        var kebab = error.Code.ToLowerInvariant().Replace('_', '-');
        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine($"code: {error.Code}");
        sb.AppendLine($"http_status: {error.HttpStatus}");
        sb.AppendLine($"kind: {error.Kind}");
        sb.AppendLine($"category: {error.Category}");
        sb.AppendLine($"emitted_by:");
        foreach (var factory in error.EmittedBy)
            sb.AppendLine($"  - \"{factory}\"");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {error.Code}");
        sb.AppendLine();
        sb.AppendLine($"> **HTTP {error.HttpStatus}** | `{error.Kind}` | Category: {error.Category}");
        sb.AppendLine();

        // Auto-generated section
        sb.AppendLine("<!-- AUTO-GEN-START -->");
        sb.AppendLine($"**Default message:** {error.MessageTemplate}");
        sb.AppendLine();
        sb.AppendLine($"**Documentation URL:** [`https://docs.clustral.kube.it.com/errors/{kebab}`](https://docs.clustral.kube.it.com/errors/{kebab})");
        sb.AppendLine("<!-- AUTO-GEN-END -->");
        sb.AppendLine();

        // Hand-authored sections (stubs)
        sb.AppendLine("## What this means");
        sb.AppendLine();
        sb.AppendLine("<!-- TODO: Explain what this error means in plain language. -->");
        sb.AppendLine();
        sb.AppendLine("## Why it happens");
        sb.AppendLine();
        sb.AppendLine("<!-- TODO: List the common causes. -->");
        sb.AppendLine();
        sb.AppendLine("## How to fix");
        sb.AppendLine();
        sb.AppendLine("<!-- TODO: Step-by-step remediation. -->");
        sb.AppendLine();
        sb.AppendLine("## Example response");
        sb.AppendLine();
        sb.AppendLine("```");
        if (error.Category is "Gateway" or "Exception Handler" || error.Code.StartsWith("AGENT") || error.Code.StartsWith("TUNNEL") || error.Code.StartsWith("NO_ROLE") || error.Code.StartsWith("AUTHENTICATION") || error.Code.StartsWith("CREDENTIAL_REVOKED") || error.Code.StartsWith("CREDENTIAL_EXPIRED") || error.Code.StartsWith("CLUSTER_MISMATCH") || error.Code.StartsWith("INVALID_CLUSTER") || error.Code.StartsWith("INVALID_TOKEN"))
        {
            // Plain text (proxy path or gateway)
            sb.AppendLine($"HTTP/1.1 {error.HttpStatus}");
            sb.AppendLine($"Content-Type: text/plain");
            sb.AppendLine($"X-Clustral-Error-Code: {error.Code}");
            sb.AppendLine($"X-Correlation-Id: <uuid>");
            sb.AppendLine($"Link: <https://docs.clustral.kube.it.com/errors/{kebab}>; rel=\"help\"");
            sb.AppendLine();
            sb.AppendLine(error.MessageTemplate);
        }
        else
        {
            // RFC 7807 (REST API)
            sb.AppendLine($"HTTP/1.1 {error.HttpStatus}");
            sb.AppendLine($"Content-Type: application/problem+json");
            sb.AppendLine($"X-Clustral-Error-Code: {error.Code}");
            sb.AppendLine($"X-Correlation-Id: <uuid>");
            sb.AppendLine();
            sb.AppendLine("{");
            sb.AppendLine($"  \"type\": \"https://docs.clustral.kube.it.com/errors/{kebab}\",");
            sb.AppendLine($"  \"title\": \"{error.Code}\",");
            sb.AppendLine($"  \"status\": {error.HttpStatus},");
            sb.AppendLine($"  \"detail\": \"{EscapeJson(error.MessageTemplate)}\"");
            sb.AppendLine("}");
        }
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## See also");
        sb.AppendLine();
        sb.AppendLine("<!-- TODO: Link to related Getting Started / CLI / Operator pages. -->");

        return sb.ToString();
    }

    private static string RenderIndex(List<ErrorEntry> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Error Reference");
        sb.AppendLine();
        sb.AppendLine("Every error response from the Clustral platform includes a machine-readable");
        sb.AppendLine("error code in the `X-Clustral-Error-Code` header (and in the RFC 7807 `type`");
        sb.AppendLine("URL for REST API responses). Click any code below for details, causes, and");
        sb.AppendLine("remediation steps.");
        sb.AppendLine();
        sb.AppendLine("| Code | HTTP | Kind | Category | Summary |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (var error in errors)
        {
            var kebab = error.Code.ToLowerInvariant().Replace('_', '-');
            var summary = error.MessageTemplate.Length > 80
                ? error.MessageTemplate[..77] + "..."
                : error.MessageTemplate;
            summary = summary.Replace("|", "\\|");
            sb.AppendLine($"| [`{error.Code}`]({kebab}.md) | {error.HttpStatus} | {error.Kind} | {error.Category} | {summary} |");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Merges auto-generated content into an existing file, preserving any
    /// hand-authored sections (What this means, Why, How to fix, See also).
    /// </summary>
    private static string MergeWithExisting(string existing, string generated, ErrorEntry error)
    {
        const string startMarker = "<!-- AUTO-GEN-START -->";
        const string endMarker = "<!-- AUTO-GEN-END -->";

        var startIdx = existing.IndexOf(startMarker, StringComparison.Ordinal);
        var endIdx = existing.IndexOf(endMarker, StringComparison.Ordinal);

        if (startIdx < 0 || endIdx < 0)
        {
            // No markers — file from a previous format. Overwrite entirely.
            return generated;
        }

        // Replace only the auto-gen section, keep everything else.
        var genStartIdx = generated.IndexOf(startMarker, StringComparison.Ordinal);
        var genEndIdx = generated.IndexOf(endMarker, StringComparison.Ordinal);

        if (genStartIdx < 0 || genEndIdx < 0)
            return generated;

        // Replace frontmatter + header + auto-gen section, keep hand-authored tail.
        var afterExistingAutoGen = existing[(endIdx + endMarker.Length)..];
        var upToGenAutoGenEnd = generated[..(genEndIdx + endMarker.Length)];

        return upToGenAutoGenEnd + afterExistingAutoGen;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static int MapKindToStatus(ResultErrorKind kind) => kind switch
    {
        ResultErrorKind.NotFound => 404,
        ResultErrorKind.Unauthorized => 401,
        ResultErrorKind.Forbidden => 403,
        ResultErrorKind.Conflict => 409,
        ResultErrorKind.BadRequest => 400,
        ResultErrorKind.Validation => 422,
        ResultErrorKind.Internal => 500,
        _ => 500,
    };

    private static string InferCategory(string methodName, string code)
    {
        if (code.StartsWith("CLUSTER") || code.StartsWith("DUPLICATE_CLUSTER")) return "Cluster & Role";
        if (code.StartsWith("ROLE") || code.StartsWith("DUPLICATE_ROLE")) return "Cluster & Role";
        if (code.StartsWith("USER")) return "User & Credential";
        if (code.StartsWith("CREDENTIAL") || code == "UNAUTHORIZED") return "User & Credential";
        if (code.StartsWith("AUTHENTICATION") || code.StartsWith("INVALID_TOKEN") || code == "FORBIDDEN") return "Auth & Proxy";
        if (code.StartsWith("NO_ROLE") || code.StartsWith("AGENT") || code.StartsWith("TUNNEL")) return "Auth & Proxy";
        if (code.StartsWith("STATIC") || code.StartsWith("PENDING") || code.StartsWith("GRANT") || code.StartsWith("REQUEST") || code.StartsWith("INVALID_DURATION")) return "Access Request";
        if (code == "VALIDATION_ERROR" || code.StartsWith("INVALID_FORMAT") || code == "INTERNAL_ERROR") return "Validation & Generic";
        return "Other";
    }

    private static string EscapeJson(string s) => s.Replace("\"", "\\\"").Replace("\n", " ");

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Clustral.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: relative to this tool's location.
        var toolDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(toolDir, "..", "..", "..", "..", ".."));
    }
}

internal record ErrorEntry
{
    public required string Code { get; init; }
    public required ResultErrorKind Kind { get; init; }
    public required int HttpStatus { get; init; }
    public required string MessageTemplate { get; init; }
    public required List<string> EmittedBy { get; init; }
    public required string Category { get; init; }
}
