using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameCheckOutFlow
{
    /// <summary>
    /// Checks a savegame out, or takes a copy of it: the take-over question, the mod folder, the slot
    /// modal, the claim, then the mods. Where the snapshot is not the head it is restored as a new
    /// head first. Failures are reported here.
    /// </summary>
    /// <param name="currentUserId">
    /// Who is asking, so a claim of their own is not taken from "somebody". Null where it could not be
    /// read, and then any holder is asked about.
    /// </param>
    /// <param name="nameOf">What a savegame is called, read off the caller's own list. Null where it is not in it.</param>
    /// <param name="changed">
    /// Called whenever the repo moved under the caller, including before the slot modal is offered
    /// again, so the caller's list is the one it names savegames from.
    /// </param>
    Task CheckOutAsync(
        Repo repo,
        SavegameDto savegame,
        int snapshotNumber,
        SavegameCheckOutMode mode,
        string? currentUserId,
        Func<Guid, string?> nameOf,
        Func<Task> changed,
        CancellationToken cancellationToken);
}
