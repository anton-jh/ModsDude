using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Repos;

public class CheckNameTakenV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/check-name-taken", CheckNameTaken)
            .RequireAuthorization()
            .WithTags("Repos");
    }


    private static async Task<Results<Ok<CheckNameTakenResponse>, Forbidden<CustomProblemDetails>>> CheckNameTaken(
        CheckNameTakenRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Repo names are unique across the whole system, so answering this for anyone signed in is an
        // existence oracle over every repo name there is. It exists only to let a repo be named before
        // it is created, so it is gated on exactly what creating one is gated on.
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .CreateRepo())
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var isTaken = await dbContext.Repos.CheckNameIsTaken(new(request.Name), cancellationToken);

        return TypedResults.Ok(new CheckNameTakenResponse(isTaken));
    }


    public record CheckNameTakenRequest(string Name);
    public record CheckNameTakenResponse(bool IsTaken);
}
