using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>The publishes this machine has sent and not yet seen answered, so they can be repeated.</summary>
public interface ISavegamePendingPublishes
{
    /// <summary>
    /// The publish to send for these bytes from this slot: the unanswered one where it was for the
    /// same bytes, and otherwise a new one, recorded before it is returned.
    /// </summary>
    /// <exception cref="InvalidOperationException">No such game is configured on this machine.</exception>
    SavegamePendingPublish Begin(GameIdentity game, SavegameSlotRef slot, string contentHash);

    /// <summary>Forgets a publish once its answer has been acted on. Idempotent.</summary>
    void Complete(GameIdentity game, Guid requestId);
}
