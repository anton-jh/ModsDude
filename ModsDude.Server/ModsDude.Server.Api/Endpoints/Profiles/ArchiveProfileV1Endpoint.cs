using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// Puts a profile away.
/// </summary>
/// <remarks>
/// <para>
/// <b>The only way to make a profile go away</b>, and deliberately not a delete: a profile carries a
/// history that makes every one of its revisions reproducible, and that is not something to lose by
/// clicking the wrong row. Permanent deletion is a second act, taken from the archive.
/// </para>
/// <para>
/// <b>Nothing else changes.</b> The revisions stay, the mod versions they pin stay pinned, an
/// instance tracking it goes on tracking it and a savegame following it goes on following it. It is
/// out of the lists and it has given up its name, and that is all - anything more would make the
/// archive a second kind of deletion wearing a gentler word.
/// </para>
/// <para>
/// <b>Admin.</b> Making the group's shared work disappear from everybody's list is not part of
/// running a repo, which is the same line permanent deletion and revision pruning sit on.
/// </para>
/// </remarks>
public class ArchiveProfileV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/profiles/{profileId:guid}/archive", Archive)
            .WithTags("Profiles");
    }


    private static async Task<Results<Ok<ProfileDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Archive(
        Guid repoId, Guid profileId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Admin))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        profile.Archive(timeService.Now());

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(ProfileDto.FromModel(profile));
    }
}
