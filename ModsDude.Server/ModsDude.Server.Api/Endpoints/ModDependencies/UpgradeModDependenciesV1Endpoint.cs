using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.ModDependencies;

/// <summary>
/// "Apply all updates": moves a profile's dependencies to the latest version of each mod, and
/// <b>skips every locked one entirely</b>.
/// </summary>
/// <remarks>
/// <para>
/// Skipping rather than sweeping locked mods in is what lets Save carry no prompt at all — a batch
/// that cannot contain an unintended version change has nothing to ask about. Sweeping them in and
/// prompting at save re-asks a question the user already answered, every time, which is how a safety
/// prompt turns into noise people learn to dismiss. Changing a locked version stays a deliberate
/// per-row act through <c>PUT .../modDependencies/{modId}</c>.
/// </para>
/// <para>
/// A batch rather than N requests because a profile holds one to two thousand mods, and the outcomes
/// come back per dependency — including which were skipped and which of the two locks did it — so
/// that the client can render "Update 47 mods · 3 locked, skipped" without a second query.
/// </para>
/// </remarks>
public class UpgradeModDependenciesV1Endpoint : IEndpoint
{
    /// <summary>
    /// Only bounds an explicit list; omitting one means the whole profile, which is bounded by the
    /// profile itself. Set well above the one to two thousand mods a profile is expected to hold, so
    /// that it rejects a runaway request rather than a real one.
    /// </summary>
    private const int _maximumBatchSize = 5000;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/profiles/{profileId:guid}/modDependencies/upgrade", Upgrade)
            .WithTags("ModDependencies");
    }


    private static async Task<Results<Ok<UpgradeModDependenciesResponse>, BadRequest<CustomProblemDetails>>> Upgrade(
        Guid repoId, Guid profileId,
        UpgradeModDependenciesRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        var requestedModIds = request.ModIds?.Select(x => new ModId(x)).ToList();

        if (requestedModIds is not null && requestedModIds.Count > _maximumBatchSize)
        {
            return TypedResults.BadRequest(Problems.BatchTooLarge(requestedModIds.Count, _maximumBatchSize));
        }

        var profile = await dbContext.Profiles.GetWithModDependenciesAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{profileId}' found in repo '{repoId}'"));
        }

        List<ModId> considered = requestedModIds is null
            ? [.. profile.ModDependencies.Select(x => x.ModVersion.ModId).OrderBy(x => x.Value)]
            : [.. requestedModIds.Distinct()];

        var dependencies = profile.ModDependencies.ToDictionary(x => x.ModVersion.ModId);

        var upgradable = considered
            .Where(x => dependencies.TryGetValue(x, out var dependency) && !dependency.IsEffectivelyLocked)
            .ToList();

        var latestVersions = await dbContext.ModVersions.GetLatestVersionOfEachAsync(new RepoId(repoId), upgradable, cancellationToken);

        var results = new List<ModDependencyUpgradeDto>(considered.Count);

        foreach (var modId in considered)
        {
            if (!dependencies.TryGetValue(modId, out var dependency))
            {
                results.Add(new ModDependencyUpgradeDto(modId.Value, ModDependencyUpgradeOutcome.NotInProfile, null, null, false, false));
                continue;
            }

            var currentVersionId = dependency.ModVersion.Id.Value;

            if (dependency.IsEffectivelyLocked)
            {
                results.Add(new ModDependencyUpgradeDto(
                    modId.Value,
                    ModDependencyUpgradeOutcome.SkippedLocked,
                    currentVersionId,
                    null,
                    dependency.Locked,
                    dependency.ModVersion.Locked));

                continue;
            }

            // The candidate set is each mod's latest version rather than all of its siblings. Both
            // CanBeUpgraded and Upgrade only ever look at the highest sequence number, and
            // materializing every version of two thousand mods — each dragging its owned attribute
            // and image collections — to read one row per mod is exactly the cost a batch form
            // exists to avoid.
            ModVersion[] candidates = latestVersions.TryGetValue(modId, out var latest) ? [latest] : [];

            if (!dependency.CanBeUpgraded(candidates))
            {
                results.Add(new ModDependencyUpgradeDto(modId.Value, ModDependencyUpgradeOutcome.AlreadyLatest, currentVersionId, currentVersionId, false, false));
                continue;
            }

            dependency.Upgrade(candidates);

            results.Add(new ModDependencyUpgradeDto(
                modId.Value,
                ModDependencyUpgradeOutcome.Upgraded,
                currentVersionId,
                dependency.ModVersion.Id.Value,
                false,
                false));
        }

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(new UpgradeModDependenciesResponse(results));
    }


    /// <param name="ModIds">
    /// The dependencies to consider, or <c>null</c> for every dependency in the profile — "apply all
    /// updates" on a two-thousand-mod profile should not have to name them all.
    /// </param>
    public record UpgradeModDependenciesRequest(IEnumerable<string>? ModIds);

    public record UpgradeModDependenciesResponse(IEnumerable<ModDependencyUpgradeDto> Results);

    /// <param name="FromVersionId">The version the dependency was on, or <c>null</c> if there was no dependency.</param>
    /// <param name="ToVersionId">The version it is on now, or <c>null</c> where nothing moved it.</param>
    /// <param name="LockedInProfile">This profile pinned it. The user's own answer, so it is never overridden here.</param>
    /// <param name="LockedByMod">The adapter marked the mod itself version-sensitive at registration.</param>
    public record ModDependencyUpgradeDto(
        string ModId,
        ModDependencyUpgradeOutcome Outcome,
        string? FromVersionId,
        string? ToVersionId,
        bool LockedInProfile,
        bool LockedByMod);

    public enum ModDependencyUpgradeOutcome
    {
        Upgraded,
        AlreadyLatest,
        SkippedLocked,

        /// <summary>
        /// A mod the request named that the profile does not depend on. Reported rather than ignored
        /// so that a client working from a stale list can see which of its rows are gone.
        /// </summary>
        NotInProfile
    }
}
