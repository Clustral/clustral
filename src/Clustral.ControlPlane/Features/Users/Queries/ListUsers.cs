using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Users.Queries;

public record ListUsersQuery : IQuery<Result<UserListResponse>>;

public sealed class ListUsersHandler(IUserRepository users)
    : IRequestHandler<ListUsersQuery, Result<UserListResponse>>
{
    public async Task<Result<UserListResponse>> Handle(ListUsersQuery request, CancellationToken ct)
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
