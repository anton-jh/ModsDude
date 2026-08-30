using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// Which of a profile's pins have a newer version behind them, and which of those a batch update is
/// allowed to move.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Newer" is whatever the repo says it is.</b> Ordering is settled server-side, from the
/// adapter's comparer and the user's arbitration, and arrives as
/// <see cref="CatalogModVersion.SequenceNumber"/>. Re-deriving it here would make clients on
/// different adapter versions disagree about what an update is, which is the one thing a batch
/// action must not do. See docs/09-mod-catalog.md#a-note-on-update-available.
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
        IEnumerable<CatalogModVersion> catalog)
    {
        return Plan(pins, Registered(catalog));
    }

    /// <summary>
    /// The same, against a set already grouped by <see cref="Registered"/>. An editor replans on
    /// every toggle, and regrouping a repo's several thousand versions each time would be work the
    /// catalog has already done.
    /// </summary>
    public static ProfileModUpdatePlan Plan(
        IEnumerable<ProfileModPin> pins,
        IReadOnlyDictionary<ModKey, IReadOnlyList<CatalogModVersion>> registered)
    {
        var available = new List<ProfileModUpdate>();
        var skipped = new List<ProfileModUpdate>();

        foreach (var pin in pins)
        {
            if (FindUpdate(pin, registered) is not ProfileModUpdate update)
            {
                continue;
            }

            (update.Lock.IsLocked ? skipped : available).Add(update);
        }

        return new ProfileModUpdatePlan(available, skipped);
    }

    /// <summary>
    /// The newest registered version of one pinned mod, when it is newer than what the pin holds.
    /// </summary>
    /// <remarks>
    /// A pin whose own version the repo does not hold - one still waiting to be imported - is left
    /// alone: without a sequence number for it there is nothing to be newer than, and offering an
    /// "update" from a version that does not exist yet would be a guess.
    /// </remarks>
    public static ProfileModUpdate? FindUpdate(
        ProfileModPin pin,
        IReadOnlyDictionary<ModKey, IReadOnlyList<CatalogModVersion>> registered)
    {
        if (registered.TryGetValue(pin.ModId, out var versions) is false)
        {
            return null;
        }

        var current = versions.FirstOrDefault(x => x.VersionId == pin.VersionId);

        if (current is null)
        {
            return null;
        }

        var newest = versions[^1];

        return newest.SequenceNumber > current.SequenceNumber
            ? new ProfileModUpdate(pin.ModId, pin.VersionId, newest.VersionId, pin.Lock)
            : null;
    }

    /// <summary>
    /// The repo's own versions, oldest first. Unregistered rows are dropped rather than ordered:
    /// they carry no sequence number, so the repo has not placed them and nothing here may.
    /// </summary>
    public static IReadOnlyDictionary<ModKey, IReadOnlyList<CatalogModVersion>> Registered(
        IEnumerable<CatalogModVersion> catalog)
    {
        return catalog
            .Where(x => x is { IsOnServer: true, SequenceNumber: not null })
            .GroupBy(x => x.ModId)
            .ToDictionary(
                x => x.Key,
                IReadOnlyList<CatalogModVersion> (x) => [.. x.OrderBy(version => version.SequenceNumber)]);
    }
}

/// <param name="From">What the profile pins today.</param>
/// <param name="To">The repo's newest version of the same mod.</param>
public sealed record ProfileModUpdate(ModKey ModId, ModVersionKey From, ModVersionKey To, ProfileModLock Lock);

/// <param name="Available">Unlocked, so "apply all updates" moves them.</param>
/// <param name="Skipped">
/// Locked, so the batch leaves them alone and says how many it left. Reached deliberately through
/// that count rather than swept in and prompted about.
/// </param>
public sealed record ProfileModUpdatePlan(
    IReadOnlyList<ProfileModUpdate> Available,
    IReadOnlyList<ProfileModUpdate> Skipped)
{
    public static ProfileModUpdatePlan Empty { get; } = new([], []);

    /// <summary>Every pin with a newer version, locked or not - what the header counts.</summary>
    public int Count => Available.Count + Skipped.Count;

    public bool HasAny => Count > 0;
}
