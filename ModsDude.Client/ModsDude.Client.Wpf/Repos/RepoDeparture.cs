using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Repos;

/// <inheritdoc cref="IRepoDeparture"/>
public sealed class RepoDeparture(
    IRepoWork repoWork,
    IHeldSavegameClaims heldClaims,
    ISavegameBindingStore bindings,
    ISavegameStore savegames,
    IGameRepository games,
    ISavegameCheckInFlow checkInFlow,
    Lazy<IModalService> modalService) : IRepoDeparture
{
    public async Task<bool> RunAsync(Repo repo, RepoDepartureKind kind, Func<CancellationToken, Task> depart, CancellationToken cancellationToken)
    {
        if (await RefuseIfBusyAsync(repo) is false)
        {
            return false;
        }

        var held = await HeldAsync(repo, cancellationToken);

        if (held.Count == 0 ? await ConfirmAsync(repo, kind) is false : await ConfirmWithHeldAsync(repo, kind, held, cancellationToken) is false)
        {
            return false;
        }

        using var claim = repoWork.TryClaim(repo.Id, kind is RepoDepartureKind.Leave ? $"Leaving '{repo.Name}'" : $"Archiving '{repo.Name}'");

        if (claim is null)
        {
            await RefuseIfBusyAsync(repo);

            return false;
        }

        await depart(cancellationToken);

        return true;
    }


    private async Task<bool> RefuseIfBusyAsync(Repo repo)
    {
        if (repoWork.DescribeBusy(repo.Id) is not string holder)
        {
            return true;
        }

        await modalService.Value.Show(ConfirmationModalViewModel.Refusal(
            $"'{repo.Name}' is busy",
            $"{holder} is running."));

        return false;
    }

    private async Task<bool> ConfirmAsync(Repo repo, RepoDepartureKind kind)
    {
        var modal = kind is RepoDepartureKind.Leave
            ? new ConfirmationModalViewModel(
                $"Leave {repo.Name}?",
                "You lose access to its mods, profiles and savegames. Rejoining needs a new invite.",
                IconKind.Warning,
                "Leave",
                "Cancel")
            : ConfirmationModalViewModel.ConfirmArchive(repo.Name, "repo");

        await modalService.Value.Show(modal);

        return modal.Result;
    }

    /// <returns>Whether to go ahead: chosen anyway, or chosen once every save was checked in.</returns>
    private async Task<bool> ConfirmWithHeldAsync(Repo repo, RepoDepartureKind kind, IReadOnlyList<HeldSave> held, CancellationToken cancellationToken)
    {
        var leave = kind is RepoDepartureKind.Leave;

        var modal = new ConfirmLossModalViewModel(
            leave ? $"Leave {repo.Name}?" : $"Archive {repo.Name}?",
            leave
                ? "These savegames are checked out here and cannot be checked in after you leave:"
                : "These savegames are checked out here and cannot be checked in after the repo is archived:",
            [.. held.Select(x => x.Name)],
            leave ? "Leave anyway" : "Archive anyway",
            "Cancel",
            leave ? "Check in all, then leave" : "Check in all, then archive");

        await modalService.Value.Show(modal);

        switch (modal.Choice)
        {
            case ConfirmLossChoice.Confirm:
                return true;

            case ConfirmLossChoice.Alternative:
                return await CheckInAllAsync(repo, held, cancellationToken);

            default:
                return false;
        }
    }

    /// <summary>Checks each save in as its own savegame operation, and goes ahead only when none is left checked out here.</summary>
    private async Task<bool> CheckInAllAsync(Repo repo, IReadOnlyList<HeldSave> held, CancellationToken cancellationToken)
    {
        foreach (var save in held)
        {
            await checkInFlow.CheckInHeldAsync(save.Game, save.SavegameId, save.Name, cancellationToken);
        }

        var stillHeld = await HeldAsync(repo, cancellationToken);

        if (stillHeld.Count == 0)
        {
            return true;
        }

        await modalService.Value.Show(ConfirmationModalViewModel.Refusal(
            "Still checked out",
            $"Nothing was changed in '{repo.Name}'. Still checked out here: {string.Join(", ", stillHeld.Select(x => $"'{x.Name}'"))}."));

        return false;
    }

    private async Task<IReadOnlyList<HeldSave>> HeldAsync(Repo repo, CancellationToken cancellationToken)
    {
        var ids = heldClaims.Capture().Where(x => x.RepoId == repo.Id).Select(x => x.SavegameId).ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        await savegames.EnsureLoadedAsync(repo.Id, cancellationToken);

        return [.. ids
            .Select(id => (Id: id, Game: games.Games.FirstOrDefault(x => bindings.GetBinding(x.Identity, id) is not null)))
            .Where(x => x.Game is not null)
            .Select(x => new HeldSave(x.Game!, x.Id, savegames.Find(repo.Id, x.Id)?.Name ?? "A savegame"))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.SavegameId)];
    }


    private sealed record HeldSave(Game Game, Guid SavegameId, string Name);
}
