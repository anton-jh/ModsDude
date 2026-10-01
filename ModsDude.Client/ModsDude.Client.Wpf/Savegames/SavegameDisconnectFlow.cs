using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// The modal host is taken lazily because it is the shell itself, which is composed from services
/// that may want this one.
/// </remarks>
public sealed class SavegameDisconnectFlow(
    ISavegameHolds savegameHolds,
    Lazy<IModalService> modalService) : ISavegameDisconnectFlow
{
    public async Task<bool> DisconnectAsync(Game game, Guid savegameId, string savegameName, string? folderName)
    {
        var where = folderName is string named ? $"the '{named}' folder" : "a folder this game's settings no longer name";

        var modal = new ConfirmationModalViewModel(
            $"Stop tracking '{savegameName}'?",
            $"Your copy stays exactly where it is, in {where}, and becomes an ordinary save of your own - ModsDude stops recognising it. "
              + $"The claim on '{savegameName}' is not handed back, so nobody else can take it until you do. "
              + "Point the settings back at that folder instead if you want to check it in.",
            IconKind.Warning,
            "Stop tracking it - nothing on disk changes",
            "Leave it connected");

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return false;
        }

        return savegameHolds.Forget(game, savegameId);
    }
}
