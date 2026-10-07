using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// The three things that write a revision - saving a mod list, restoring an older revision, and
/// copying one into a profile branched off another - differ only in where the pins come from. What
/// they share is here.
/// </summary>
internal static class ProfileRevisionWrites
{
    /// <summary>
    /// Only bounds a runaway request. A profile of one to two thousand mods is what this targets, so
    /// the cap sits well above that rather than at it.
    /// </summary>
    public const int MaximumMods = 5000;


    /// <summary>
    /// Turns a request's pins into dependencies, which means resolving each one to the registered
    /// version behind it - a dependency can only ever name a version the repo already holds.
    /// </summary>
    /// <remarks>
    /// The versions are materialized rather than projected, because a dependency's foreign key is a
    /// navigation to a tracked entity and EF cannot be handed a key on its own. That is the one
    /// place a save reads whole <see cref="ModVersion"/> rows, and it is why it happens once per
    /// save rather than once per mod.
    /// </remarks>
    public static async Task<ResolvedPins> ResolveAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        IReadOnlyList<ProfileModPin> pins,
        CancellationToken cancellationToken)
    {
        if (pins.Count > MaximumMods)
        {
            return new ResolvedPins(null, Problems.BatchTooLarge(pins.Count, MaximumMods));
        }

        var duplicate = pins
            .GroupBy(x => x.ModId)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicate is not null)
        {
            return new ResolvedPins(null, Problems.ModPinnedTwice(duplicate.Key));
        }

        if (pins.Count == 0)
        {
            return new ResolvedPins([], null);
        }

        var versions = await dbContext.ModVersions.GetVersionsAsync(
            repoId,
            [.. pins.Select(x => x.ModId).Distinct()],
            [.. pins.Select(x => x.VersionId).Distinct()],
            cancellationToken);

        var byKey = versions.ToDictionary(x => (x.ModId, x.Id));

        var dependencies = new List<ModDependency>(pins.Count);

        foreach (var pin in pins)
        {
            if (byKey.TryGetValue((pin.ModId, pin.VersionId), out var version) is false)
            {
                return new ResolvedPins(null, Problems.NotFound.With(
                    x => x.Detail = $"No version '{pin.VersionId.Value}' of mod '{pin.ModId.Value}' is registered in repo '{repoId.Value}'"));
            }

            dependencies.Add(new ModDependency
            {
                ModVersion = version,
                Locked = pin.Locked
            });
        }

        return new ResolvedPins(dependencies, null);
    }

    /// <summary>
    /// The revision as the history renders it. The author is looked up rather than carried on the
    /// revision, and it is the request's own user every time this runs, so the lookup is answered
    /// from the change tracker that authorization already loaded them into.
    /// </summary>
    public static async Task<ProfileRevisionDto> ToDtoAsync(
        ApplicationDbContext dbContext,
        ProfileRevision revision,
        CancellationToken cancellationToken)
    {
        var author = await dbContext.Users.GetAsync(revision.CreatedBy, cancellationToken);

        return ProfileRevisionDto.FromModel(revision, ProfileRevisionReads.Describe(revision.CreatedBy, author is null ? null : UserNameplate.Of(author)));
    }


    /// <summary>The latest save or restore <paramref name="userId"/> made on the profile, tracked so it can be replaced.</summary>
    public static Task<ProfileRevisionRequest?> FindRequestAsync(
        ApplicationDbContext dbContext,
        Profile profile,
        UserId userId,
        CancellationToken cancellationToken)
    {
        return dbContext.ProfileRevisionRequests.FirstOrDefaultAsync(
            x => x.RepoId == profile.RepoId && x.ProfileId == profile.Id && x.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// After a commit failed: the same request sent twice at once, where the other copy committed
    /// first and recorded the answer this one is given too. Null where the failure was anything else.
    /// </summary>
    public static Task<ProfileRevisionRequest?> FindRepeatAsync(
        ApplicationDbContext dbContext,
        Profile profile,
        UserId userId,
        ProfileRevisionRequestId requestId,
        CancellationToken cancellationToken)
    {
        return dbContext.ProfileRevisionRequests.AsNoTracking().FirstOrDefaultAsync(
            x => x.RepoId == profile.RepoId && x.ProfileId == profile.Id && x.UserId == userId && x.RequestId == requestId,
            cancellationToken);
    }

    public static void RecordRequest(
        ApplicationDbContext dbContext,
        ProfileRevisionRequest? previous,
        Profile profile,
        UserId userId,
        ProfileRevisionRequestId requestId,
        DateTime now,
        RevisionNumber answeredWith)
    {
        if (previous is null)
        {
            dbContext.ProfileRevisionRequests.Add(new ProfileRevisionRequest(profile.RepoId, profile.Id, userId, requestId, now, answeredWith));
        }
        else
        {
            previous.Replace(requestId, now, answeredWith);
        }
    }

    public static async Task<Results<Ok<ProfileRevisionDto>, BadRequest<CustomProblemDetails>>> AnswerAgainAsync(
        ApplicationDbContext dbContext,
        ProfileRevisionRequest original,
        CancellationToken cancellationToken)
    {
        var revision = await ProfileRevisionReads.GetAsync(dbContext, original.RepoId, original.ProfileId, original.AnsweredWith, cancellationToken);

        return revision is null
            ? TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail =
                $"Revision {original.AnsweredWith.Value} of profile '{original.ProfileId.Value}', which this request was answered with, has since been deleted."))
            : TypedResults.Ok(revision);
    }


    /// <summary>Either the dependencies to snapshot, or the reason there are none.</summary>
    public record ResolvedPins(IReadOnlyList<ModDependency>? Dependencies, CustomProblemDetails? Problem);
}
