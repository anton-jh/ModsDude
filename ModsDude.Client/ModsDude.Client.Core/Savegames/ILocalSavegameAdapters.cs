using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// The savegame adapter for a game. Hydrating one needs a repo's base settings, so the lookup goes
/// through whichever loaded repo serves the game's scope.
/// </summary>
public interface ILocalSavegameAdapters
{
    /// <returns>Null where no loaded repo serves the game, or where its adapter has no savegames.</returns>
    ILocalSavegameAdapter? TryGet(Game game);

    /// <inheritdoc cref="TryGet(Game)"/>
    ILocalSavegameAdapter? TryGet(GameIdentity identity);
}
