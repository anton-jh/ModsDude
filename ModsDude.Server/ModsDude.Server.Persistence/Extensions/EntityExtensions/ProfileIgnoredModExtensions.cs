using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;

/// <summary>
/// Reading and replacing the mods a profile ignores, and keeping them clear of the ones it pins.
/// </summary>
public static class ProfileIgnoredModExtensions
{
    public static async Task<HashSet<ModId>> GetModIdsAsync(
        this DbSet<ProfileIgnoredMod> dbSet,
        RepoId repoId, ProfileId profileId,
        CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId)
            .Select(x => x.ModId)
            .ToListAsync(cancellationToken);

        return [.. rows];
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
    /// Makes the profile ignore exactly <paramref name="desired"/>. The caller commits.
    /// </summary>
    /// <remarks>
    /// <b>The whole list, like a save.</b> It belongs to one profile and is written by whoever edits
    /// it, so what goes over the wire is the list the page showed and the server records exactly that.
    /// Two members writing at once is last write wins, which is a fair price for state that only
    /// decides which rows an editor offers.
    /// </remarks>
    public static async Task ReplaceIgnoredAsync(
        this ApplicationDbContext dbContext,
        Profile profile, IReadOnlyCollection<ModId> desired,
        CancellationToken cancellationToken)
    {
        var wanted = desired.ToHashSet();

        var current = await dbContext.ProfileIgnoredMods
            .Where(x => x.RepoId == profile.RepoId && x.ProfileId == profile.Id)
            .ToListAsync(cancellationToken);

        dbContext.ProfileIgnoredMods.RemoveRange(current.Where(x => wanted.Contains(x.ModId) is false));

        var held = current.Select(x => x.ModId).ToHashSet();

        foreach (var modId in wanted.Where(x => held.Contains(x) is false))
        {
            dbContext.ProfileIgnoredMods.Add(new ProfileIgnoredMod(profile.RepoId, profile.Id, modId));
        }
    }

    /// <summary>
    /// Stops ignoring whatever a revision pins. Called by everything that writes one, in the same
    /// unit of work, so a mod pinned by a save that never mentioned the ignore list is not left as
    /// both.
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
    /// Takes a mod off every ignore list in the repo. A mod that is removed and later imported again
    /// must not come back already hidden.
    /// </summary>
    public static async Task ReleaseModAsync(
        this ApplicationDbContext dbContext,
        RepoId repoId, ModId modId,
        CancellationToken cancellationToken)
    {
        var doomed = await dbContext.ProfileIgnoredMods
            .Where(x => x.RepoId == repoId && x.ModId == modId)
            .ToListAsync(cancellationToken);

        dbContext.ProfileIgnoredMods.RemoveRange(doomed);
    }
}
