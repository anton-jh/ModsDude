using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Games;

/// <remarks>
/// <para>
/// <b>Savegames from a repo you are not in leave with the game.</b> They can never be checked in, so
/// waiting for that would keep the game connected forever. They are named in the confirmation, and
/// their slots stay exactly as they are: a later write into one finds an unrecognised save, which asks
/// first and goes to the Recycle Bin.
/// </para>
/// <para>
/// A savegame from any other repo refuses the disconnect, since its check-out record is the only thing
/// that lets its play be checked in.
/// </para>
/// <para>
/// The modal host is taken lazily because it is the shell itself, which is composed from services
/// that may want this one.
/// </para>
/// </remarks>
public sealed class GameDisconnectFlow(
    IGameRepository games,
    ISavegameBindingStore bindings,
    IGoneRepos goneRepos,
    ISavegameOperations operations,
    IDriftMonitor driftMonitor,
    Lazy<IModalService> modalService,
    ILogger<GameDisconnectFlow> logger) : IGameDisconnectFlow
{
    public async Task<string?> DisconnectAsync(Game game)
    {
        var held = bindings.GetBindings(game.Identity);

        if (held.Any(x => goneRepos.IsGone(x.RepoId) is false))
        {
            return SavegameRefusals.GameHoldsSavegame(game.Name, held, goneRepos);
        }

        // Forgetting a binding mid-operation would pull the record out from under it.
        foreach (var binding in held)
        {
            if (operations.DescribeBusy(game, binding.SavegameId) is string busy)
            {
                return $"{busy} is running. Try again once it has finished.";
            }
        }

        var drifted = driftMonitor.Drifted;
        var letGo = held.OrderBy(x => x.Slot.ToString(), StringComparer.Ordinal).ToList();

        var modal = ConfirmationModalViewModel.ConfirmDisconnectGame(
            game.Name,
            [.. letGo.Select(x => StopsTracking(x, drifted))]);

        await modalService.Value.Show(modal);

        if (modal.Result is false)
        {
            return null;
        }

        var outcome = games.Delete(game, letGo.Select(x => x.SavegameId).ToHashSet());

        switch (outcome)
        {
            case DisconnectOutcome.Busy:
                logger.LogInformation("Did not disconnect game {Game}: something is working on its folders.", game.Identity);

                return $"'{game.Name}' is busy. Try again once it has finished.";

            case DisconnectOutcome.HoldsSavegame:
                logger.LogInformation("Did not disconnect game {Game}: a savegame was checked out since the confirmation.", game.Identity);

                return SavegameRefusals.GameHoldsSavegame(game.Name, bindings.GetBindings(game.Identity), goneRepos);
        }

        foreach (var binding in letGo)
        {
            logger.LogInformation(
                "Game {Game} was disconnected and stopped tracking savegame {Savegame} of repo {Repo}; slot {Slot} was left alone.",
                game.Identity, binding.SavegameId, binding.RepoId, binding.Slot);
        }

        return null;
    }


    private static string StopsTracking(SavegameCheckoutBinding binding, IReadOnlyList<TargetDrift> drifted)
    {
        var name = HeldSavegameDrift.SlotName(drifted, binding.SavegameId) is string slotName
            ? $"'{slotName}'"
            : $"The savegame in slot '{binding.Slot.Slot}'";

        return HeldSavegameDrift.HasUncheckedInPlay(drifted, binding.SavegameId)
            ? $"{name} is from a repo you are not in and stays in its slot, no longer tracked. Its progress since check-out stays only on this disk."
            : $"{name} is from a repo you are not in and stays in its slot, no longer tracked.";
    }
}
