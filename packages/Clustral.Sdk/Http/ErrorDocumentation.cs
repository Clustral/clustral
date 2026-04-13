namespace Clustral.Sdk.Http;

/// <summary>
/// Central location for error-documentation URL construction. Every error
/// response (plain text, RFC 7807, unhandled exception) points at
/// <c>{BaseUrl}{code-kebab}</c> so developers can go from error code to
/// explanation + remediation in one click.
///
/// <para>RFC 7807 § 3 says the <c>type</c> field <i>should</i> be a URI
/// "that identifies the problem type". Using a resolvable HTTPS URL
/// (instead of an opaque <c>urn:</c>) matches Stripe, Azure, Microsoft
/// Graph, and every enterprise API that integrators learn from first.</para>
///
/// <para>On the plain-text proxy path we also emit <c>Link: &lt;url&gt;;
/// rel="help"</c> — a standardized way (RFC 8288) for any HTTP client to
/// discover documentation without parsing our JSON.</para>
///
/// <para>The base URL is overridable via environment (<c>CLUSTRAL_ERROR_DOCS_BASE</c>)
/// or appsettings (<c>Errors:DocsBaseUrl</c>) for air-gapped deployments
/// that host the docs internally. Configure once at process startup via
/// <see cref="SetBaseUrl"/>.</para>
/// </summary>
public static class ErrorDocumentation
{
    /// <summary>
    /// Default documentation base. Points at the public docs; on-prem
    /// customers can override at startup via <see cref="SetBaseUrl"/>.
    /// </summary>
    public const string DefaultBaseUrl = "https://docs.clustral.kube.it.com/errors/";

    private static string _baseUrl = DefaultBaseUrl;

    /// <summary>
    /// Sets the error-documentation base URL. Call once at process startup
    /// (e.g., in <c>Program.cs</c>) if the default public URL isn't
    /// appropriate. The value should end with a trailing slash.
    /// </summary>
    public static void SetBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return;
        _baseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
    }

    /// <summary>Current documentation base URL (trailing slash guaranteed).</summary>
    public static string BaseUrl => _baseUrl;

    /// <summary>
    /// Returns the documentation URL for a given error code. Lower-cases the
    /// code and replaces underscores with hyphens (kebab-case) to match the
    /// docs-site URL convention (e.g., <c>NO_ROLE_ASSIGNMENT</c> →
    /// <c>https://docs.clustral.kube.it.com/errors/no-role-assignment</c>).
    /// </summary>
    public static string UrlFor(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return _baseUrl;
        return _baseUrl + code.ToLowerInvariant().Replace('_', '-');
    }
}
