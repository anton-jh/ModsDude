using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <remarks>
/// One entry per slot, and never replaced: a publish whose answer was lost may have landed, so the
/// caller finds out what became of it before a different publish from the same slot can begin.
/// </remarks>
public sealed class SavegamePendingPublishes(IPersistedGameState state) : ISavegamePendingPublishes
{
    public SavegamePendingPublish? Find(GameIdentity game, SavegameSlotRef slot)
        => state.Read(game, persisted => persisted?.SavegamePendingPublishes.FirstOrDefault(x => x.Slot.Addresses(slot)));

    public SavegamePendingPublish Begin(GameIdentity game, SavegameSlotRef slot, Guid repoId, string contentHash, string slotHash)
    {
        SavegamePendingPublish? pending = null;

        state.Update(game, persisted =>
        {
            if (persisted is null)
            {
                throw new InvalidOperationException($"No local game '{game}' to publish a savegame from.");
            }

            var existing = persisted.SavegamePendingPublishes.FirstOrDefault(x => x.Slot.Addresses(slot));

            if (existing is not null)
            {
                if (existing.RepoId != repoId || existing.ContentHash != contentHash)
                {
                    throw new InvalidOperationException(
                        $"Slot '{slot}' of game '{game}' has an unanswered publish of other contents, which has to be settled first.");
                }

                pending = existing;

                return false;
            }

            pending = new SavegamePendingPublish(slot, repoId, contentHash, slotHash, Guid.NewGuid(), Guid.NewGuid());
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
