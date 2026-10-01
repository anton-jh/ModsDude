using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Savegames;

internal static class SavegameRefusals
{
    /// <summary>
    /// The refusal for a verb that needs the repo's game on this machine. For a game that connects by
    /// itself, not connected can only mean not installed here.
    /// </summary>
    /// <param name="why">Why the verb needs the game, said before how to connect it.</param>
    public static ConfirmationModalViewModel NotConnected(Repo repo, string why)
    {
        var automatic = GameRepository.ConnectsAutomatically(repo.Adapter);

        return ConfirmationModalViewModel.Refusal(
            automatic
                ? $"{repo.Adapter.GameDisplayName} was not found on this machine"
                : "No game is connected here",
            $"{why} " + (automatic
                ? "Launch it once so it creates its data folder, then try again."
                : $"Use 'Connect game' in {repo.Name} first."));
    }
}
