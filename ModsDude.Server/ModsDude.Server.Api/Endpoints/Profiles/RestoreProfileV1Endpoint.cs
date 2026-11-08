using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

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
            .WithTags("Profiles")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok<ProfileDto>, BadRequest<CustomProblemDetails>>> Restore(
        Guid repoId, Guid profileId,
        RestoreRequest? request,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (RequestChecks.OptionalName(request?.Name) is { } invalid)
        {
            return TypedResults.BadRequest(invalid);
        }

        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        ProfileName? name = request?.Name is { Length: > 0 } requested
            ? new ProfileName(requested)
            : null;

        // Already restored: a repeat of this request is answered as the first was. A rename of a live
        // profile is UpdateProfile's, which checks the version it was made against.
        if (profile.IsArchived is false)
        {
            return name is null || name == profile.Name
                ? TypedResults.Ok(ProfileDto.FromModel(profile))
                : TypedResults.BadRequest(Problems.NotArchived("Profile", profileId).With(x => x.Detail = $"Profile '{profileId}' is not archived, so it cannot be restored."));
        }

        var wanted = name ?? profile.Name;

        // Checked here so the answer is a message rather than a unique violation, and enforced again
        // by the filtered index underneath - which is what makes it true when two admins restore two
        // archived profiles of one name at the same moment.
        if (await dbContext.Profiles.CheckNameIsTaken(new RepoId(repoId), profile.Id, wanted, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(wanted.Value));
        }

        profile.Restore(name);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.BadRequest(Problems.ProfileChanged);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.BadRequest(Problems.NameTaken(wanted.Value));
        }

        return TypedResults.Ok(ProfileDto.FromModel(profile));
    }
}
