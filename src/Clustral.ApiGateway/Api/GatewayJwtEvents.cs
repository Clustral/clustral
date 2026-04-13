using Clustral.Sdk.Results;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Clustral.ApiGateway.Api;

/// <summary>
/// Custom <see cref="JwtBearerEvents"/> that replace ASP.NET's default empty
/// 401 / 403 bodies with path-aware, self-speaking responses via
/// <see cref="GatewayErrorWriter"/>.
///
/// This class is a pure classifier: it maps a failed-auth exception to a
/// <see cref="ResultError"/> and then hands the error to the writer. All
/// user-facing text lives in <see cref="ResultErrors"/> — the single source
/// of truth for Clustral error messages. If a message needs changing, change
/// the factory, not this file.
/// </summary>
public static class GatewayJwtEvents
{
    public static JwtBearerEvents Create() => new()
    {
        OnChallenge = async ctx =>
        {
            ctx.HandleResponse();  // suppress ASP.NET's default empty 401

            var hint = HintFor(ctx.HttpContext);
            var error = ClassifyAuthFailure(ctx.AuthenticateFailure, hint);
            await GatewayErrorWriter.WriteAsync(ctx.HttpContext,
                statusCode: StatusCodes.Status401Unauthorized,
                code: error.Code,
                message: error.Message);
        },

        OnForbidden = async ctx =>
        {
            var error = ResultErrors.AuthorizationFailed(HintFor(ctx.HttpContext));
            await GatewayErrorWriter.WriteAsync(ctx.HttpContext,
                statusCode: StatusCodes.Status403Forbidden,
                code: error.Code,
                message: error.Message);
        },
    };

    /// <summary>
    /// Picks the right remediation hint based on the request path: proxy
    /// requests get "clustral kube login" guidance, everything else gets
    /// "clustral login".
    /// </summary>
    private static ResultErrors.LoginHint HintFor(HttpContext ctx) =>
        GatewayErrorWriter.IsProxyPath(ctx.Request.Path)
            ? ResultErrors.LoginHint.Kubectl
            : ResultErrors.LoginHint.Api;

    /// <summary>
    /// Maps a <see cref="SecurityTokenException"/> subtype to the matching
    /// <see cref="ResultErrors"/> factory. No strings live here — every
    /// message is a call to a catalog factory so text lives in one place.
    /// </summary>
    internal static ResultError ClassifyAuthFailure(Exception? ex, ResultErrors.LoginHint hint) => ex switch
    {
        null                                   => ResultErrors.AuthenticationRequired(hint),
        SecurityTokenExpiredException          => ResultErrors.TokenExpired(hint),
        SecurityTokenInvalidSignatureException => ResultErrors.TokenInvalidSignature(hint),
        SecurityTokenInvalidIssuerException    => ResultErrors.TokenInvalidIssuer(hint),
        SecurityTokenInvalidAudienceException  => ResultErrors.TokenInvalidAudience(hint),
        SecurityTokenNotYetValidException      => ResultErrors.TokenNotYetValid(hint),
        SecurityTokenNoExpirationException     => ResultErrors.TokenMissingExpiration(hint),
        SecurityTokenException                 => ResultErrors.TokenValidationFailed(ex.Message, hint),
        _                                      => ResultErrors.AuthenticationRequired(hint),
    };
}
