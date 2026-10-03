using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <remarks>
/// One entry per slot: a slot's newer bytes are a different publish, and the older one can no longer
/// be repeated from it.
/// </remarks>
public sealed class SavegamePendingPublishes(IPersistedGameState state) : ISavegamePendingPublishes
{
    public SavegamePendingPublish Begin(GameIdentity game, SavegameSlotRef slot, string contentHash)
    {
        SavegamePendingPublish? pending = null;

        state.Update(game, persisted =>
        {
            if (persisted is null)
            {
                throw new InvalidOperationException($"No local game '{game}' to publish a savegame from.");
            }

            pending = persisted.SavegamePendingPublishes.FirstOrDefault(x => x.Slot.Addresses(slot) && x.ContentHash == contentHash);

            if (pending is not null)
            {
                return false;
            }

            pending = new SavegamePendingPublish(slot, contentHash, Guid.NewGuid(), Guid.NewGuid());

            persisted.SavegamePendingPublishes.RemoveAll(x => x.Slot.Addresses(slot));
            persisted.SavegamePendingPublishes.Add(pending);

            return true;
        });

        return pending!;
    }

    public void Complete(GameIdentity game, Guid requestId)
    {
        state.Update(game, persisted =>
            persisted is not null && persisted.SavegamePendingPublishes.RemoveAll(x => x.RequestId == requestId) > 0);
    }
}
