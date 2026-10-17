using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;

/// <summary>
/// Reading and replacing a profile's ignore list - the mods it ignores and the versions it dismisses -
/// and keeping the ignored mods clear of the ones it pins.
/// </summary>
public static class ProfileIgnoreListExtensions
{
    public static async Task<ProfileIgnoreList> GetIgnoreListAsync(
        this ApplicationDbContext dbContext,
        RepoId repoId, ProfileId profileId,
        CancellationToken cancellationToken)
    {
        var mods = await dbContext.ProfileIgnoredMods
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId)
            .Select(x => x.ModId)
            .ToListAsync(cancellationToken);

        var versions = await dbContext.ProfileDismissedVersions
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId)
            .Select(x => new DismissedVersion(x.ModId, x.VersionId))
            .ToListAsync(cancellationToken);

        return new ProfileIgnoreList(mods.ToHashSet(), versions.ToHashSet());
    }

    /// <summary>
    /// Which of <paramref name="desired"/> the profile's head pins, and so cannot be ignored. Only the head:
    /// an older revision may well have pinned a mod that is ignored now, which is the point of ignoring it.
    /// </summary>
    public static async Task<List<ModId>> FindPinnedAsync(
        this ApplicationDbContext dbContext,
        Profile profile, IEnumerable<ModId> desired,
        CancellationToken cancellationToken)
    {
        var pinned = await dbContext.ProfileRevisions.GetPinnedModIdsAsync(
            profile.RepoId, profile.Id, profile.HeadRevision, cancellationToken);

        return [.. desired.Where(pinned.Contains)];
    }

    /// <summary>
    /// Makes the profile's ignore list exactly <paramref name="desired"/>. The caller commits.
    /// </summary>
    /// <remarks>
    /// <b>The whole list, like a save.</b> It belongs to one profile and is written by whoever edits
    /// it, so what goes over the wire is the list the page showed and the server records exactly that.
    /// </remarks>
    public static async Task ReplaceIgnoreListAsync(
        this ApplicationDbContext dbContext,
        Profile profile, ProfileIgnoreList desired,
        CancellationToken cancellationToken)
    {
        var currentMods = await dbContext.ProfileIgnoredMods
            .Where(x => x.RepoId == profile.RepoId && x.ProfileId == profile.Id)
            .ToListAsync(cancellationToken);

        dbContext.ProfileIgnoredMods.RemoveRange(currentMods.Where(x => desired.Mods.Contains(x.ModId) is false));

        var heldMods = currentMods.Select(x => x.ModId).ToHashSet();

        foreach (var modId in desired.Mods.Where(x => heldMods.Contains(x) is false))
        {
            dbContext.ProfileIgnoredMods.Add(new ProfileIgnoredMod(profile.RepoId, profile.Id, modId));
        }

        var currentVersions = await dbContext.ProfileDismissedVersions
            .Where(x => x.RepoId == profile.RepoId && x.ProfileId == profile.Id)
            .ToListAsync(cancellationToken);

        dbContext.ProfileDismissedVersions.RemoveRange(currentVersions.Where(x => desired.Versions.Contains(x.Key) is false));

        var heldVersions = currentVersions.Select(x => x.Key).ToHashSet();

        foreach (var version in desired.Versions.Where(x => heldVersions.Contains(x) is false))
        {
            dbContext.ProfileDismissedVersions.Add(new ProfileDismissedVersion(profile.RepoId, profile.Id, version.ModId, version.VersionId));
        }
    }

    /// <summary>
    /// Stops ignoring whatever a revision pins. Called by everything that writes one, in the same
    /// unit of work, so a mod pinned by a save that never mentioned the ignore list is not left as
    /// both. Dismissed versions are left alone: they hold for pinned mods too.
    /// </summary>
    public static async Task ReleasePinnedAsync(
        this ApplicationDbContext dbContext,
        Profile profile, IEnumerable<ProfileModPin> pins,
        CancellationToken cancellationToken)
    {
        var pinned = pins.Select(x => x.ModId).ToList();

        if (pinned.Count == 0)
        {
            return;
        }

        var doomed = await dbContext.ProfileIgnoredMods
            .Where(x => x.RepoId == profile.RepoId && x.ProfileId == profile.Id && pinned.Contains(x.ModId))
            .ToListAsync(cancellationToken);

        dbContext.ProfileIgnoredMods.RemoveRange(doomed);
    }

    /// <summary>
    /// Takes a mod and its dismissed versions off every ignore list in the repo. A mod that is removed
    /// and later imported again must not come back already hidden.
    /// </summary>
    public static async Task ReleaseModAsync(
        this ApplicationDbContext dbContext,
        RepoId repoId, ModId modId,
        CancellationToken cancellationToken)
    {
        var mods = await dbContext.ProfileIgnoredMods
            .Where(x => x.RepoId == repoId && x.ModId == modId)
            .ToListAsync(cancellationToken);

        dbContext.ProfileIgnoredMods.RemoveRange(mods);

        var versions = await dbContext.ProfileDismissedVersions
            .Where(x => x.RepoId == repoId && x.ModId == modId)
            .ToListAsync(cancellationToken);

        dbContext.ProfileDismissedVersions.RemoveRange(versions);
    }

    /// <summary>
    /// Takes one version off every ignore list in the repo, for the same reason as
    /// <see cref="ReleaseModAsync"/>.
    /// </summary>
    public static async Task ReleaseModVersionAsync(
        this ApplicationDbContext dbContext,
        RepoId repoId, ModId modId, ModVersionId versionId,
        CancellationToken cancellationToken)
    {
        var versions = await dbContext.ProfileDismissedVersions
            .Where(x => x.RepoId == repoId && x.ModId == modId && x.VersionId == versionId)
            .ToListAsync(cancellationToken);

        dbContext.ProfileDismissedVersions.RemoveRange(versions);
    }
}

/// <param name="Mods">The mods the profile ignores.</param>
/// <param name="Versions">The exact versions it is not offered as updates.</param>
public sealed record ProfileIgnoreList(IReadOnlySet<ModId> Mods, IReadOnlySet<DismissedVersion> Versions)
{
    public bool IsSameAs(ProfileIgnoreList other) => Mods.SetEquals(other.Mods) && Versions.SetEquals(other.Versions);
}
