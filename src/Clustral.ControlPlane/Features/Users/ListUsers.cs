using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Repositories;
using MediatR;

namespace Clustral.ControlPlane.Features.Users;

public record ListUsersQuery : IRequest<UserListResponse>;

public sealed class ListUsersHandler(IUserRepository users)
    : IRequestHandler<ListUsersQuery, UserListResponse>
{
    public async Task<UserListResponse> Handle(ListUsersQuery request, CancellationToken ct)
    {
        var allUsers = await users.ListAsync(ct);

        var response = allUsers
            .OrderBy(u => u.Email)
            .Select(u => new UserResponse(
                u.Id, u.Email ?? u.KeycloakSubject, u.DisplayName, u.CreatedAt, u.LastSeenAt))
            .ToList();

        return new UserListResponse(response);
    }
}
