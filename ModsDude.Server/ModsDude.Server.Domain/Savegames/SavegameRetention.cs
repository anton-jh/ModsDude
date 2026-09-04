using ModsDude.Server.Domain.Exceptions;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// What a version looks like to the retention policy: its number, and whether somebody named it.
/// </summary>
public readonly record struct SavegameVersionRetention(SavegameVersionNumber Number, bool IsLabelled);

/// <summary>
/// Decides which versions of one savegame are old enough to drop. Pure, and separate from storage
/// for the same reason <see cref="Mods.BlobReclamation"/> is: the decision is the part that can
/// destroy somebody's play, and it is the part that can be tested without a storage account.
/// </summary>
/// <remarks>
/// <para>
/// A savegame's history is a set of backups rather than a coordination artifact, which is what makes
/// pruning legitimate here and not for profile revisions - an old revision has to stay reproducible,
/// an old backup does not.
/// </para>
/// <para>
/// Two things are never dropped. <b>The head</b>, because dropping the current save is not a
/// retention policy, it is data loss. And <b>anything labelled</b>, because labelling a version is
/// the gesture by which a person says to keep it - a rule small enough to hold in your head, and one
/// that reuses a field the history already has.
/// </para>
/// <para>
/// Labelled versions are exempt rather than counted, so labelling ten old ones does not silently
/// push out the recent ones the policy exists to keep.
/// </para>
/// </remarks>
public static class SavegameRetention
{
    /// <summary>
    /// How many versions a repo keeps unless it says otherwise. Ten is roughly a fortnight of
    /// evenings, which is as far back as anybody has ever wanted to go.
    /// </summary>
    public const int DefaultVersionsKept = 10;


    /// <param name="versions">Every version the savegame currently has, in any order.</param>
    /// <param name="head">The current version, which is retained whatever else is true of it.</param>
    /// <param name="keep">How many to retain by recency. At least one.</param>
    /// <returns>
    /// The versions that may be dropped, oldest first - the order they should be deleted in, so that
    /// a sweep interrupted halfway has removed the least interesting ones.
    /// </returns>
    public static IReadOnlyList<SavegameVersionNumber> PlanPrune(
        IEnumerable<SavegameVersionRetention> versions,
        SavegameVersionNumber head,
        int keep = DefaultVersionsKept)
    {
        if (keep < 1)
        {
            throw new DomainValidationException($"A savegame cannot be kept to {keep} versions.");
        }

        var ordered = versions
            .OrderByDescending(x => x.Number)
            .ToList();

        // The window counts unlabelled versions only. A labelled one is kept whatever happens, so
        // letting it occupy a place in the window would mean somebody who named the last two saves
        // ends up with two backups instead of ten - the policy quietly doing less than it says
        // because of a gesture that was supposed to keep more.
        var retainedByRecency = ordered
            .Where(x => !x.IsLabelled)
            .Take(keep)
            .Select(x => x.Number)
            .ToHashSet();

        return
        [
            .. ordered
                .Where(x => !x.IsLabelled)
                .Where(x => x.Number != head)
                .Where(x => !retainedByRecency.Contains(x.Number))
                .Select(x => x.Number)
                .Reverse()
        ];
    }
}
