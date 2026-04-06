using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Users;

public record ListUsersQuery : IRequest<UserListResponse>;

public sealed class ListUsersHandler(ClustralDb db)
    : IRequestHandler<ListUsersQuery, UserListResponse>
{
    public async Task<UserListResponse> Handle(ListUsersQuery request, CancellationToken ct)
    {
        var users = await db.Users
            .Find(FilterDefinition<User>.Empty)
            .SortBy(u => u.Email)
            .ToListAsync(ct);

        var response = users.Select(u => new UserResponse(
            u.Id, u.Email ?? u.KeycloakSubject, u.DisplayName, u.CreatedAt, u.LastSeenAt)).ToList();

        return new UserListResponse(response);
    }
}
