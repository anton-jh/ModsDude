using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameDisconnectFlow
{
    /// <summary>
    /// Stops tracking a hold in a folder the settings no longer name. Nothing on disk changes and the
    /// claim is kept.
    /// </summary>
    /// <param name="folderName">The folder holding it, where the caller can name it.</param>
    /// <returns>False where the modal was dismissed, or there was nothing to forget.</returns>
    Task<bool> DisconnectAsync(Game game, Guid savegameId, string savegameName, string? folderName);
}
