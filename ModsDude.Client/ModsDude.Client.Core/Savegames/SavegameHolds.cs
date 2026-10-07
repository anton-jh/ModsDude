using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameHolds(
    ISavegamesClient savegamesClient,
    ISavegameStore store,
    ISavegameBindingStore bindings,
    ILocalSavegameAdapters adapters,
    ILogger<SavegameHolds> logger)
    : ISavegameHolds
{
    public async Task ReleaseAsync(Game game, Guid repoId, Guid savegameId, CancellationToken ct)
    {
        await store.WriteAsync(repoId, token => savegamesClient.DiscardSavegameCheckoutV1Async(repoId, savegameId, token), ct);

        bindings.ClearBinding(game.Identity, savegameId);
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
