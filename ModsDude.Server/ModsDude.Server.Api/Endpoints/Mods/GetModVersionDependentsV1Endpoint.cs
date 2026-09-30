using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Persistence.DbContexts;

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
            .WithTags("Mods")
            .RequireRepoLevel(RepoMembershipLevel.Guest);
    }


    private static Task<Ok<ModDependentsDto>> Get(
        Guid repoId, string modId, string versionId,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
        => ModDependentsReads.GetAsync(repoId, modId, new ModVersionId(versionId), dbContext, cancellationToken);
}
