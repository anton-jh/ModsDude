using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Persistence.DbContexts;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Mods;

/// <inheritdoc cref="ModDependentsReads"/>
/// <summary>
/// Every revision that pins any version of a mod - what a refused "delete the whole mod" needs.
/// </summary>
public class GetModDependentsV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/mods/{modId}/dependents", Get)
            .WithTags("Mods");
    }


    private static Task<Results<Ok<ModDependentsDto>, Forbidden<CustomProblemDetails>>> Get(
        Guid repoId, string modId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
        => ModDependentsReads.GetAsync(repoId, modId, null, claimsPrincipal, dbContext, cancellationToken);
}
