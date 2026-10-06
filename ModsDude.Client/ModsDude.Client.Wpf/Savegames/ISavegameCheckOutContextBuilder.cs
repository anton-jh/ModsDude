using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameCheckOutContextBuilder
{
    /// <summary>
    /// Everything the check-out slot step needs about one game: its slots and their safety, what the
    /// mod folder would have to do, and how far the save's revision is from the profile's.
    /// </summary>
    /// <param name="pinnedRevision">The revision the save goes onto in compatibility mode, null for latest.</param>
    /// <param name="verdict">How far latest has moved from the played revision, where it has.</param>
    /// <param name="heldNames">What the savegames this game holds are called.</param>
    /// <param name="checkedInFirst">The savegames checked in before the check-out, whose slots it may then write into.</param>
    Task<SavegameCheckOutContext> BuildAsync(
        Repo repo,
        SavegameDto savegame,
        Game game,
        SavegameCheckOutMode mode,
        int? pinnedRevision,
        SavegameCompatibilityVerdict? verdict,
        IReadOnlyDictionary<Guid, HeldSavegameName> heldNames,
        IReadOnlySet<Guid> checkedInFirst,
        CancellationToken cancellationToken);
}
