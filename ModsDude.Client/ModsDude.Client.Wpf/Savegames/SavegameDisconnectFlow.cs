using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// The modal host is taken lazily because it is the shell itself, which is composed from services
/// that may want this one.
/// </remarks>
public sealed class SavegameDisconnectFlow(
    ISavegameHolds savegameHolds,
    IToastService toasts,
    Lazy<IModalService> modalService) : ISavegameDisconnectFlow
{
    public async Task<bool> DisconnectAsync(Game game, Guid savegameId, string? savegameName, SavegameDisconnectReason reason)
    {
        var quoted = savegameName is string name ? $"'{name}'" : "this savegame";

        var (message, kept) = reason switch
        {
            SavegameDisconnectReason.FolderNotNamed folder => (
                $"Your copy stays exactly where it is, in {Where(folder)}, and becomes an ordinary save of your own - ModsDude stops recognising it. "
                    + $"The claim on {quoted} is not handed back, so nobody else can take it until you do. "
                    + "Point the settings back at that folder instead if you want to check it in.",
                "The save is still on this disk, and the claim is still yours."),

            SavegameDisconnectReason.RepoGone gone => (
                "The save stays in its slot as an ordinary save of your own, and ModsDude stops recognising it. "
                    + "It can't be checked in to its repo any more."
                    + (gone.Played ? " The play since it was checked out stays only on this disk." : ""),
                "The save is still on this disk."),

            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

        var modal = new ConfirmationModalViewModel(
            $"Stop tracking {quoted}?",
            message,
            IconKind.Warning,
            "Stop tracking it - nothing on disk changes",
            "Keep tracking it");

        await modalService.Value.Show(modal);

        if (modal.Result is false || savegameHolds.Forget(game, savegameId) is false)
        {
            return false;
        }

        toasts.Show($"ModsDude has stopped tracking {quoted}. {kept}");

        return true;
    }


    private static string Where(SavegameDisconnectReason.FolderNotNamed folder)
        => folder.FolderName is string named ? $"the '{named}' folder" : "a folder this game's settings no longer name";
}
