using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// A profile's ignore list - the mods it ignores and the versions it dismisses: read whole, replaced whole.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a revision, and written on its own.</b> It says nothing about what the profile applies -
/// only which rows and updates the editor offers - so it neither appears in the history nor has to be
/// atomic with a save. The editor writes it after the save that carried the mod list has succeeded,
/// and not otherwise. See <see cref="ProfileIgnoredMod"/> and <see cref="ProfileDismissedVersion"/>.
/// </para>
/// <para>
/// <b>A pinned mod cannot also be ignored</b>, and only what the head pins counts. Writing a list that
/// overlaps it is refused, and saving or restoring a revision releases whatever it newly pins, so the
/// two never overlap however the writes interleave. Dismissed versions have no such rule.
/// </para>
/// </remarks>
public class GetProfileIgnoreListV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profiles/{profileId:guid}/ignoreList", Get)
            .WithTags("Profiles")
            .RequireRepoLevel(RepoMembershipLevel.Guest);
    }


    private static async Task<Results<Ok<ProfileIgnoreListDto>, BadRequest<CustomProblemDetails>>> Get(
        Guid repoId, Guid profileId,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{profileId}' found in repo '{repoId}'"));
        }

        var list = await dbContext.GetIgnoreListAsync(profile.RepoId, profile.Id, cancellationToken);

        return TypedResults.Ok(ProfileIgnoreListDto.From(list, profile.IgnoredVersion));
    }
}

/// <inheritdoc cref="GetProfileIgnoreListV1Endpoint"/>
public class SetProfileIgnoreListV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/profiles/{profileId:guid}/ignoreList", Set)
            .WithTags("Profiles")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok<ProfileIgnoreListDto>, BadRequest<CustomProblemDetails>>> Set(
        Guid repoId, Guid profileId,
        SetProfileIgnoreListRequest request,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{profileId}' found in repo '{repoId}'"));
        }

        var desired = new ProfileIgnoreList(
            request.ModIds.Select(x => new ModId(x)).ToHashSet(),
            request.DismissedVersions.Select(x => new DismissedVersion(new ModId(x.ModId), new ModVersionId(x.VersionId))).ToHashSet());

        foreach (var count in new[] { desired.Mods.Count, desired.Versions.Count })
        {
            if (count > ProfileRevisionWrites.MaximumMods)
            {
                return TypedResults.BadRequest(Problems.BatchTooLarge(count, ProfileRevisionWrites.MaximumMods));
            }
        }

        var current = await dbContext.GetIgnoreListAsync(profile.RepoId, profile.Id, cancellationToken);

        // Already so: a repeat of this request - a retry, a second click - is answered as the first was.
        if (current.IsSameAs(desired))
        {
            return TypedResults.Ok(ProfileIgnoreListDto.From(current, profile.IgnoredVersion));
        }

        if (profile.IgnoredVersion != request.ExpectedVersion)
        {
            return TypedResults.BadRequest(Problems.ProfileChanged);
        }

        var overlap = await dbContext.FindPinnedAsync(profile, desired.Mods, cancellationToken);

        if (overlap.Count > 0)
        {
            return TypedResults.BadRequest(Problems.IgnoredModPinned(overlap));
        }

        try
        {
            await dbContext.ReplaceIgnoreListAsync(profile, desired, cancellationToken);
            profile.NoteIgnoredReplaced();

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another write of this list got in first. Which one it was is the version's to say.
            return TypedResults.BadRequest(Problems.ProfileChanged);
        }

        return TypedResults.Ok(ProfileIgnoreListDto.From(desired, profile.IgnoredVersion));
    }


    /// <param name="ModIds">Every mod the profile should ignore. Anything absent stops being ignored.</param>
    /// <param name="DismissedVersions">Every version it should not be offered as an update. Anything absent is offered again.</param>
    /// <param name="ExpectedVersion">The version of the list the change was made against. Another one is refused.</param>
    public record SetProfileIgnoreListRequest(IEnumerable<string> ModIds, IEnumerable<DismissedVersionDto> DismissedVersions, int ExpectedVersion);
}
