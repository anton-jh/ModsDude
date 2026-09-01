using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Persistence.DbContexts;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Users;

/// <summary>
/// Who the caller is, in this system's terms.
/// </summary>
/// <remarks>
/// The display name is the token's own, so a client could paint that much without asking. The tag
/// is not: it is derived from the subject id by a rule that lives on this side, and it is what tells
/// this user apart from the other person of the same name they may one day stand next to in a member
/// list. Nothing else reaches it for the caller themselves - <see cref="GetUsersV1Endpoint"/>
/// deliberately returns everyone <i>except</i> them.
/// <para>
/// It also carries <c>IsTrusted</c>, which no other route exposes: it is the caller's own
/// permission to create repos, and a client that cannot read it can only offer the option and let
/// the server refuse after the form is filled in.
/// </para>
/// </remarks>
public class GetCurrentUserV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("users/me", GetCurrentUser)
            .WithTags("Users");
    }


    private async Task<Ok<CurrentUserDto>> GetCurrentUser(
        ApplicationDbContext dbContext,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        // UserLoadingMiddleware has already provisioned the row for anyone who got this far, so a
        // miss here is not a new user - it is a token whose subject this system has no record of.
        var user = await dbContext.Users.FindAsync([claimsPrincipal.GetUserId()], cancellationToken)
            ?? throw new NotAuthenticatedException();

        return TypedResults.Ok(CurrentUserDto.FromModel(user));
    }
}
