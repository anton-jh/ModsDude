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
    ISavegameOperations operations,
    IToastService toasts,
    Lazy<IModalService> modalService) : ISavegameDisconnectFlow
{
    public async Task<bool> DisconnectAsync(Game game, Guid savegameId, string? savegameName, SavegameDisconnectReason reason)
    {
        var quoted = savegameName is string name ? $"'{name}'" : "this savegame";

        // Forgetting a binding mid-operation would pull the record out from under a check-in.
        if (operations.DescribeBusy(game, savegameId) is string busy)
        {
            await modalService.Value.Show(ConfirmationModalViewModel.Refusal(
                "Something else is using that savegame",
                $"{busy} is running. Try again once it has finished."));

            return false;
        }

        var (message, kept) = reason switch
        {
            SavegameDisconnectReason.FolderNotNamed folder => (
                $"Your copy stays on disk, in {Where(folder)}. It stays checked out to you. "
                    + "To check it in instead, add the folder back to the game settings.",
                $"Stopped tracking {quoted}. It is still checked out to you."),

            SavegameDisconnectReason.RepoGone gone => (
                "Your copy stays in its slot. It can no longer be checked in."
                    + (gone.Played ? " Its progress since check-out stays only on this disk." : ""),
                $"Stopped tracking {quoted}"),

            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

        var modal = new ConfirmationModalViewModel(
            $"Stop tracking {quoted}?",
            message,
            IconKind.Warning,
            "Stop tracking",
            "Cancel");

        await modalService.Value.Show(modal);

        if (modal.Result is false || savegameHolds.Forget(game, savegameId) is false)
        {
            return false;
        }

        toasts.Show(kept);

        return true;
    }


    private static string Where(SavegameDisconnectReason.FolderNotNamed folder)
        => folder.FolderName is string named ? $"the '{named}' folder" : "a folder no longer in the game settings";
}
