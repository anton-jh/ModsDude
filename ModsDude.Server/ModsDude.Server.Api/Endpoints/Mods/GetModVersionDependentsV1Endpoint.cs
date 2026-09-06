using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Persistence.DbContexts;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Mods;

/// <inheritdoc cref="ModDependentsReads"/>
/// <summary>
/// Every revision that pins one version - what a refused "delete this version" needs. Narrower than
/// the mod-level answer on purpose: listing a sibling version's dependents after this one was
/// refused would name revisions that have nothing to do with it.
/// </summary>
public class GetModVersionDependentsV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/mods/{modId}/versions/{versionId}/dependents", Get)
            .WithTags("Mods");
    }


    private static Task<Results<Ok<ModDependentsDto>, Forbidden<CustomProblemDetails>>> Get(
        Guid repoId, string modId, string versionId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
        => ModDependentsReads.GetAsync(repoId, modId, new ModVersionId(versionId), claimsPrincipal, dbContext, cancellationToken);
}
