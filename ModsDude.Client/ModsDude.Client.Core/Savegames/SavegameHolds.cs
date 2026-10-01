using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameHolds(
    ISavegamesClient savegamesClient,
    ISavegameBindingStore bindings,
    ILocalSavegameAdapters adapters,
    ILogger<SavegameHolds> logger)
    : ISavegameHolds
{
    public async Task ReleaseAsync(Game game, Guid repoId, Guid savegameId, CancellationToken ct)
    {
        await savegamesClient.DiscardSavegameCheckoutV1Async(repoId, savegameId, ct);

        bindings.ClearBinding(game.Identity, savegameId);
    }

    public async Task<MakeSavegameCurrentResponse> MakeCurrentAsync(
        IReadOnlyList<Game> games,
        SavegameDto savegame,
        CancellationToken ct)
    {
        var response = await savegamesClient.MakeSavegameCurrentV1Async(savegame.RepoId, savegame.Id, ct);

        foreach (var game in games)
        {
            if (bindings.GetBinding(game.Identity, savegame.Id) is not SavegameCheckoutBinding binding
                || binding.TargetRevision is null)
            {
                continue;
            }

            bindings.SetBinding(game.Identity, binding with { TargetRevision = null });

            logger.LogInformation(
                "Savegame {Savegame} is current again; game {Game} stopped pinning its mod folder to revision {Revision}.",
                savegame.Id, game.Identity, binding.TargetRevision);
        }

        return response;
    }

    public bool Forget(Game game, Guid savegameId)
    {
        var forgotten = bindings.Forget(game.Identity, savegameId);

        if (forgotten)
        {
            logger.LogInformation(
                "Game {Game} stopped tracking savegame {Savegame}; the slot's contents were left alone.",
                game.Identity, savegameId);
        }

        return forgotten;
    }

    public IReadOnlyList<SavegameCheckoutBinding> GetUnreachableHolds(Game game)
    {
        if (adapters.TryGet(game) is not ILocalSavegameAdapter adapter)
        {
            return [];
        }

        var targets = adapter.SavegameTargets;

        return [.. bindings.GetBindings(game.Identity).Where(x => targets[x.Slot.Target] is null)];
    }
}
