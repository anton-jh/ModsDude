using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.Savegames;

public interface IHeldSavegameNames
{
    /// <summary>
    /// What every savegame held on this game is called, read from the repo each one is in.
    /// </summary>
    /// <param name="currentRepoId">The repo in view. Savegames in any other repo are named with theirs.</param>
    /// <param name="known">Savegames of the repo in view the caller has already read, so they are not read again.</param>
    Task<IReadOnlyDictionary<Guid, HeldSavegameName>> ReadAsync(
        Game game,
        Guid currentRepoId,
        IReadOnlyCollection<SavegameDto> known,
        CancellationToken cancellationToken);
}
