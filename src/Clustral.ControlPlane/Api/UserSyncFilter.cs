using System.Security.Claims;
using Clustral.ControlPlane.Domain.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Clustral.ControlPlane.Api;

/// <summary>
/// Action filter that upserts the authenticated user in MongoDB on every
/// authenticated API request. Delegates to <see cref="UserSyncService"/>
/// for the actual upsert logic.
/// </summary>
public sealed class UserSyncFilter(UserSyncService userSync) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var subject = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                       ?? user.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

            if (!string.IsNullOrEmpty(subject))
            {
                var displayName = user.Claims.FirstOrDefault(c => c.Type == "name")?.Value
                               ?? user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
                               ?? user.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
                var email = user.Claims.FirstOrDefault(c => c.Type == "email")?.Value
                         ?? user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
                         ?? user.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;

                await userSync.SyncFromOidcClaimsAsync(
                    subject, email, displayName,
                    context.HttpContext.RequestAborted);
            }
        }

        await next();
    }
}
