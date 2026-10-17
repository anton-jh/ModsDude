using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// Which of a profile's pins have a newer version behind them, and which of those a batch update is
/// allowed to move.
/// </summary>
/// <remarks>
/// <para>
/// <b>An update is an update whether or not the repo has it yet.</b> Planning is against the derived
/// order - <see cref="ModVersionSet"/> - rather than against the registered half alone, because the
/// version somebody who has just downloaded a mod came here to find is precisely the one the repo has
/// not registered. Without this it appeared in one place only: the pinned row's own dropdown.
/// </para>
/// <para>
/// <b>The rule that the repo settles ordering is untouched.</b> Registered versions keep their
/// <see cref="CatalogModVersion.SequenceNumber"/> and are handed to the derivation as fact, so an
/// ordering the repo has already arbitrated cannot come back as a guess. What the comparer adds is
/// where the unregistered versions sit, and only where it is sure: a pair it abstained on is not an
/// update, because offering a possible downgrade as one is worse than saying nothing. That is the
/// same rule <c>RepoModsPageViewModel.IsUpdate</c> applies against the repo's own newest, arrived at
/// from the other end. See docs/09-mod-catalog.md#a-note-on-update-available.
/// </para>
/// <para>
/// <b>Locked pins are not candidates at all</b>, rather than candidates the save prompts about.
/// Sweeping them in and asking at save re-asks a question the user answered when they locked the
/// mod, every single time, which is how a safety prompt becomes noise people dismiss. Skipping them
/// outright means the save that follows cannot contain an unintended version change and needs no
/// prompt. See docs/09-mod-catalog.md#batch-updates-skip-locked-mods-entirely.
/// </para>
/// </remarks>
public static class ProfileModUpdates
{
    public static ProfileModUpdatePlan Plan(
        IEnumerable<ProfileModPin> pins,
        IEnumerable<CatalogModVersion> catalog,
        IModVersionComparer comparer)
    {
        return Plan(pins, ModVersionIndex.Build(catalog, comparer));
    }

    /// <summary>
    /// The same, against an index the caller already holds. An editor replans on every toggle, and
    /// re-deriving a repo's several thousand orderings each time would be work it has already done.
    /// </summary>
    /// <param name="eligible">Which versions may be updated to. Null allows every version in the index.</param>
    public static ProfileModUpdatePlan Plan(
        IEnumerable<ProfileModPin> pins,
        IReadOnlyDictionary<ModKey, ModVersionSet> versions,
        Func<CatalogModVersion, bool>? eligible = null)
    {
        var available = new List<ProfileModUpdate>();
        var skipped = new List<ProfileModUpdate>();

        foreach (var pin in pins)
        {
            if (FindUpdate(pin, versions, eligible) is not ProfileModUpdate update)
            {
                continue;
            }

            (update.Lock.IsLocked ? skipped : available).Add(update);
        }

        return new ProfileModUpdatePlan(available, skipped);
    }

    /// <summary>
    /// The newest version of one pinned mod that the order places unambiguously after the pin.
    /// </summary>
    /// <remarks>
    /// Walked from the newest end, so a version the comparer could not place is stepped over rather
    /// than ending the search: an abstention says nothing about the versions behind it, and the one
    /// below it may well be settled. The walk stops at the pin, because nothing at or before it can
    /// be after it.
    /// </remarks>
    public static ProfileModUpdate? FindUpdate(
        ProfileModPin pin,
        IReadOnlyDictionary<ModKey, ModVersionSet> versions,
        Func<CatalogModVersion, bool>? eligible = null)
    {
        if (versions.TryGetValue(pin.ModId, out var set) is false)
        {
            return null;
        }

        for (var index = set.Order.Count - 1; index >= 0; index--)
        {
            var candidate = set.Order[index];

            if (candidate.VersionId == pin.VersionId)
            {
                return null;
            }

            if ((eligible is null || eligible(candidate)) && set.IsAfter(candidate.VersionId, pin.VersionId))
            {
                return new ProfileModUpdate(pin.ModId, pin.VersionId, candidate.VersionId, pin.Lock)
                {
                    ImportsOnSave = candidate.IsOnServer is false,
                    DownloadsFrom = candidate.IsOnServer ? null : candidate.Download?.SourceName
                };
            }
        }

        return null;
    }
}

/// <param name="From">What the profile pins today.</param>
/// <param name="To">The newest version the order places after it.</param>
public sealed record ProfileModUpdate(ModKey ModId, ModVersionKey From, ModVersionKey To, ProfileModLock Lock)
{
    /// <summary>
    /// Whether taking this update also means uploading a file. Nothing is imported until Save, so
    /// the two kinds cost differently and the band says the split rather than one total.
    /// </summary>
    public bool ImportsOnSave { get; init; }

    /// <summary>The remote source a save downloads the version from first, or null where it is already here.</summary>
    public string? DownloadsFrom { get; init; }
}

/// <param name="Available">Unlocked, so "apply all updates" moves them. Every total counts these alone.</param>
/// <param name="Skipped">
/// Locked, so they are not counted as updates at all. Reached deliberately through their own link rather
/// than swept in and prompted about.
/// </param>
public sealed record ProfileModUpdatePlan(
    IReadOnlyList<ProfileModUpdate> Available,
    IReadOnlyList<ProfileModUpdate> Skipped)
{
    public int Count => Available.Count;

    /// <summary>Of those, how many a save would have to import first.</summary>
    public int PendingCount => Available.Count(x => x.ImportsOnSave);

    /// <summary>Of those, how many a save would have to download first.</summary>
    public int DownloadCount => Available.Count(x => x.DownloadsFrom is not null);

    /// <summary>Where those downloads come from, in name order.</summary>
    public IReadOnlyList<string> DownloadSources
        => [.. Available.Select(x => x.DownloadsFrom).OfType<string>().Distinct().Order(StringComparer.Ordinal)];

    /// <summary>What <em>Update all</em> would move without uploading anything.</summary>
    public int FreeCount => Available.Count(x => x.ImportsOnSave is false);

    public bool HasAny => Count > 0;
}
