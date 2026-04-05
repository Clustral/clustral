using System.Security.Claims;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Api;

/// <summary>
/// Action filter that upserts the authenticated user in MongoDB on every
/// authenticated API request. This ensures users are created on first
/// Web UI login (via NextAuth), not just on CLI credential issuance.
/// </summary>
public sealed class UserSyncFilter(ClustralDb db) : IAsyncActionFilter
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

                var existing = await db.Users
                    .Find(u => u.KeycloakSubject == subject)
                    .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

                if (existing is null)
                {
                    await db.Users.InsertOneAsync(new User
                    {
                        Id              = Guid.NewGuid(),
                        KeycloakSubject = subject,
                        DisplayName     = displayName,
                        Email           = email,
                    }, cancellationToken: context.HttpContext.RequestAborted);
                }
                else
                {
                    var update = Builders<User>.Update
                        .Set(u => u.DisplayName, displayName)
                        .Set(u => u.Email, email)
                        .Set(u => u.LastSeenAt, DateTimeOffset.UtcNow);
                    await db.Users.UpdateOneAsync(
                        u => u.Id == existing.Id, update,
                        cancellationToken: context.HttpContext.RequestAborted);
                }
            }
        }

        await next();
    }
}
