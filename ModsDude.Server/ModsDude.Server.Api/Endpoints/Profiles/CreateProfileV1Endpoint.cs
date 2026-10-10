using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Retention;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// Creates a profile, empty or as a copy of some revision of another one. A repeat of the same request
/// returns the profile it made.
/// </summary>
/// <remarks>
/// The copy is how a profile is branched off: pick a revision, name the result, and the new
/// profile's first revision pins exactly what that one pinned. It is the same primitive as a
/// restore - materialize an old snapshot as a new revision - pointed at a new profile instead of
/// this one, which is why the two share <see cref="ProfileRevisionWrites"/>.
/// </remarks>
public class CreateProfileV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/profiles", Create)
            .WithTags("Profiles")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok<ProfileDto>, BadRequest<CustomProblemDetails>>> Create(
        Guid repoId,
        CreateProfileRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        IRetentionUpkeep retentionUpkeep,
        ILogger<CreateProfileV1Endpoint> logger,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();

        var requestId = new ProfileCreateRequestId(request.RequestId);

        // Before the name check: a repeat finds its own profile holding the name.
        if (await dbContext.Profiles.GetByCreateRequestIdAsync(new RepoId(repoId), requestId, cancellationToken) is Profile repeat)
        {
            return TypedResults.Ok(ProfileDto.FromModel(repeat));
        }

        if (await dbContext.Profiles.CheckNameIsTaken(new RepoId(repoId), new ProfileName(request.Name), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(request.Name));
        }

        IReadOnlyList<ProfileModPin> pins = [];
        ProfileId? sourceProfileId = null;
        RevisionNumber? sourceRevision = null;

        if (request.CopyFrom is CopyProfileRevisionRequest copyFrom)
        {
            var source = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(copyFrom.ProfileId), cancellationToken);
            if (source is null)
            {
                return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{copyFrom.ProfileId}' found in repo '{repoId}'"));
            }

            // Null means "whatever it holds now", so that copying the live profile does not require
            // the caller to read its head first and race with a save while doing so.
            sourceRevision = copyFrom.Revision is int requested ? new RevisionNumber(requested) : source.HeadRevision;
            sourceProfileId = source.Id;

            if (!await dbContext.ProfileRevisions.ExistsAsync(source.RepoId, source.Id, sourceRevision.Value, cancellationToken))
            {
                return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Profile '{copyFrom.ProfileId}' has no revision {sourceRevision.Value.Value}"));
            }

            pins = await dbContext.ProfileRevisions.GetPinsAsync(source.RepoId, source.Id, sourceRevision.Value, cancellationToken);
        }

        var resolved = await ProfileRevisionWrites.ResolveAsync(dbContext, new RepoId(repoId), pins, cancellationToken);
        if (resolved.Problem is not null)
        {
            return TypedResults.BadRequest(resolved.Problem);
        }

        var now = timeService.Now();
        var profile = new Profile(new RepoId(repoId), new ProfileName(request.Name), now, requestId);

        // A profile is never without a revision: an empty mod list is revision 1 pinning nothing,
        // rather than a fourth state every reader would have to handle.
        var revision = profile.CreateRevision(
            resolved.Dependencies!,
            [],
            userId,
            now,
            request.Label,
            sourceProfileId is null ? ProfileRevisionOrigin.Created : ProfileRevisionOrigin.Copied,
            sourceProfileId,
            sourceRevision);

        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(revision);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // A concurrent request took the name or this request ID between the checks above and the commit.
            dbContext.Entry(profile).State = EntityState.Detached;
            dbContext.Entry(revision).State = EntityState.Detached;

            if (await dbContext.Profiles.GetByCreateRequestIdAsync(profile.RepoId, requestId, cancellationToken) is Profile concurrent)
            {
                logger.LogInformation(exception, "Creating profile {Name} in repo {RepoId} lost to a concurrent run of the same request.", request.Name, repoId);

                return TypedResults.Ok(ProfileDto.FromModel(concurrent));
            }

            if (await dbContext.Profiles.CheckNameIsTaken(profile.RepoId, profile.Name, cancellationToken))
            {
                logger.LogInformation(exception, "Creating profile {Name} in repo {RepoId} lost the name to a concurrent request.", request.Name, repoId);

                return TypedResults.BadRequest(Problems.NameTaken(request.Name));
            }

            throw;
        }

        // A branched profile pins what it copied, which may include versions that were scheduled.
        await retentionUpkeep.ReleaseModsPinnedByAsync(profile.RepoId, profile.Id, revision.Number, cancellationToken);

        return TypedResults.Ok(ProfileDto.FromModel(profile));
    }


    /// <param name="RequestId">Generated by the client for each profile it means to create.</param>
    /// <param name="CopyFrom">
    /// Another profile in the same repo to branch off, or <c>null</c> for an empty profile.
    /// </param>
    /// <param name="Label">What to call the new profile's first revision. Optional.</param>
    public record CreateProfileRequest(Guid RequestId, string Name, CopyProfileRevisionRequest? CopyFrom, string? Label);

    /// <param name="Revision">Which revision of it to copy, or <c>null</c> for its current one.</param>
    public record CopyProfileRevisionRequest(Guid ProfileId, int? Revision);
}
