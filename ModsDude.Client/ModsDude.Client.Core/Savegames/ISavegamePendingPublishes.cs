using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>The publishes this machine has sent and not yet seen answered, so they can be repeated or settled.</summary>
public interface ISavegamePendingPublishes
{
    /// <summary>The unanswered publish from this slot, or null where there is none.</summary>
    SavegamePendingPublish? Find(GameIdentity game, SavegameSlotRef slot);

    /// <summary>
    /// The publish to send for these contents from this slot: the unanswered one where it was for the
    /// same contents, and otherwise a new one, recorded before it is returned.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No such game is configured, or the slot has an unanswered publish of other contents.
    /// </exception>
    SavegamePendingPublish Begin(GameIdentity game, SavegameSlotRef slot, Guid repoId, string contentHash, string slotHash);

    /// <summary>Forgets a publish once its answer has been acted on. Idempotent.</summary>
    void Complete(GameIdentity game, Guid requestId);
}
