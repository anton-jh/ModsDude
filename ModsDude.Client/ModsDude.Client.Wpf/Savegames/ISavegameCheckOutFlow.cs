using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameCheckOutFlow
{
    /// <summary>
    /// Checks a savegame out, or takes a copy of it: the questions first - taking it from somebody,
    /// which mod list, the mod folder, what holds it, the slot - then the check-ins, the activation,
    /// the claim and the mods. Where the snapshot is not the head it is restored as a new head first.
    /// Failures are reported here.
    /// </summary>
    /// <param name="playedRevision">The revision the snapshot was played on. Null where it follows no profile.</param>
    /// <param name="revisionMode">
    /// Which mod list the save goes onto. Null asks where the latest has moved far enough from the
    /// one the snapshot was played on, and takes the latest otherwise.
    /// </param>
    /// <param name="currentUserId">
    /// Who is asking, so a claim of their own is not taken from "somebody". Null where it could not be
    /// read, and then any holder is asked about.
    /// </param>
    /// <param name="heldNames">What the savegames the game holds are called.</param>
    Task CheckOutAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        int? playedRevision,
        SavegameCheckOutMode mode,
        SavegameRevisionMode? revisionMode,
        string? currentUserId,
        IReadOnlyDictionary<Guid, HeldSavegameName> heldNames,
        CancellationToken cancellationToken);
}
