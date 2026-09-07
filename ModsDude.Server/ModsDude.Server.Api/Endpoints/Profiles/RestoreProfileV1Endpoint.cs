using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// Brings an archived profile back into the repo's list, optionally under a new name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Member</b>, the same level as the archiving it undoes.
/// </para>
/// <para>
/// <b>This is where a name clash is resolved.</b> An archived profile gave up its name the moment it
/// was archived - several archived ones may share one, told apart by when they were put away - so
/// the name it wants back may since have been taken. Refusing with <c>name-taken</c> and letting the
/// caller supply another is the whole reason the clash was deferred to here: it is the only moment
/// somebody is present to decide.
/// </para>
/// </remarks>
public class RestoreProfileV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/profiles/{profileId:guid}/restore", Restore)
            .WithTags("Profiles");
    }


    private static async Task<Results<Ok<ProfileDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Restore(
        Guid repoId, Guid profileId,
        RestoreRequest? request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
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

        ProfileName? name = request?.Name is { Length: > 0 } requested
            ? new ProfileName(requested)
            : null;

        var wanted = name ?? profile.Name;

        // Checked here so the answer is a message rather than a unique violation, and enforced again
        // by the filtered index underneath - which is what makes it true when two admins restore two
        // archived profiles of one name at the same moment.
        if (await dbContext.Profiles.CheckNameIsTaken(new RepoId(repoId), profile.Id, wanted, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(wanted.Value));
        }

        profile.Restore(name);

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(ProfileDto.FromModel(profile));
    }
}
