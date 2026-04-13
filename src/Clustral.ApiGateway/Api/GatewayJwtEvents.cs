using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Clustral.ApiGateway.Api;

/// <summary>
/// Custom <see cref="JwtBearerEvents"/> that replace ASP.NET's default empty
/// 401 / 403 bodies with path-aware, well-described responses via
/// <see cref="GatewayErrorWriter"/>. Used by both the <c>OidcJwt</c> and
/// <c>KubeconfigJwt</c> schemes.
///
/// Classifies the underlying JWT validation exception so the response body
/// tells the client why the token was rejected (expired vs bad signature vs
/// wrong audience vs wrong issuer) rather than a bare "Unauthorized".
/// </summary>
public static class GatewayJwtEvents
{
    public static JwtBearerEvents Create() => new()
    {
        OnChallenge = async ctx =>
        {
            // Skip the default response — we write our own.
            ctx.HandleResponse();

            var (code, message) = ClassifyAuthFailure(ctx.AuthenticateFailure);
            await GatewayErrorWriter.WriteAsync(ctx.HttpContext,
                statusCode: StatusCodes.Status401Unauthorized,
                code: code,
                message: message);
        },

        OnForbidden = async ctx =>
        {
            await GatewayErrorWriter.WriteAsync(ctx.HttpContext,
                statusCode: StatusCodes.Status403Forbidden,
                code: "FORBIDDEN",
                message: "Authenticated identity is not permitted to access this resource.");
        },
    };

    private static (string Code, string Message) ClassifyAuthFailure(Exception? ex) => ex switch
    {
        null => ("AUTHENTICATION_REQUIRED",
            "Authentication required — provide a valid bearer token."),

        SecurityTokenExpiredException =>
            ("INVALID_TOKEN", "Token rejected: expired."),

        SecurityTokenInvalidSignatureException =>
            ("INVALID_TOKEN", "Token rejected: invalid signature."),

        SecurityTokenInvalidIssuerException =>
            ("INVALID_TOKEN", "Token rejected: issuer not trusted."),

        SecurityTokenInvalidAudienceException =>
            ("INVALID_TOKEN", "Token rejected: audience does not match."),

        SecurityTokenNotYetValidException =>
            ("INVALID_TOKEN", "Token rejected: not yet valid."),

        SecurityTokenNoExpirationException =>
            ("INVALID_TOKEN", "Token rejected: missing expiration claim."),

        SecurityTokenException =>
            ("INVALID_TOKEN", $"Token rejected: {ex.Message}"),

        _ => ("AUTHENTICATION_REQUIRED",
            "Authentication required — provide a valid bearer token."),
    };
}
