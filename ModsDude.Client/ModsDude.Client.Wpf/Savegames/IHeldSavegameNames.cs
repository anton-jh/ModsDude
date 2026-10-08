using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.Savegames;

public interface IHeldSavegameNames
{
    /// <summary>
    /// What every savegame held on this game is called, read from the repo each one is in.
    /// </summary>
    /// <param name="currentRepoId">
    /// The repo in view, or null where none is. Savegames in any other repo are named with theirs.
    /// </param>
    Task<IReadOnlyDictionary<Guid, HeldSavegameName>> ReadAsync(
        Game game,
        Guid? currentRepoId,
        CancellationToken cancellationToken);
}
