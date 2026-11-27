using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Savegames;

internal static class SavegameRefusals
{
    /// <summary>
    /// What to do about a held savegame standing in the way: check it in, or - where its repo is
    /// gone and it never can be - stop tracking it.
    /// </summary>
    /// <param name="it">How the sentence before named the savegame: "it", "that savegame".</param>
    public static string FreeHeld(string it, bool repoIsGone)
        => repoIsGone
            ? $"Stop tracking {it} first."
            : $"Check {it} in first.";

    /// <summary>
    /// The refusal for changing or disconnecting a game while it holds savegames: stop tracking them
    /// where every one is from a repo you are not in, and otherwise check in.
    /// </summary>
    public static string GameHoldsSavegame(string gameName, IReadOnlyList<SavegameCheckoutBinding> held, IGoneRepos goneRepos)
        => $"'{gameName}' has a savegame checked out. " + FreeHeld("it", held.All(x => goneRepos.IsGone(x.RepoId)));

    /// <summary>
    /// The refusal for a verb that needs the repo's game on this machine. For a game that connects by
    /// itself, not connected can only mean not installed here.
    /// </summary>
    public static ConfirmationModalViewModel NotConnected(Repo repo)
    {
        var automatic = GameRepository.ConnectsAutomatically(repo.Adapter);

        return ConfirmationModalViewModel.Refusal(
            automatic
                ? $"{repo.Adapter.GameDisplayName} was not found on this machine"
                : "No game is connected here",
            automatic
                ? "Launch the game once, then try again."
                : $"Use 'Connect game' in {repo.Name} first.");
    }
}
