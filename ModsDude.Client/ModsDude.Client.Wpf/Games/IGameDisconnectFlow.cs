using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Games;

public interface IGameDisconnectFlow
{
    /// <summary>
    /// Asks, then disconnects the game, letting go of the savegames held from repos you are not in.
    /// </summary>
    /// <returns>Why it was refused, for the page to show where the user acted. Null where it was disconnected or cancelled.</returns>
    Task<string?> DisconnectAsync(Game game);
}
