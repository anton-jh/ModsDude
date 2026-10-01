using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>What the slots of a game hold, as the slot picker and the verbs that write into them see it.</summary>
public interface ISavegameSlots
{
    /// <summary>
    /// Every slot across every savegame folder the game reaches, occupied or not, in picker order.
    /// </summary>
    Task<IReadOnlyList<GameSavegameSlot>> GetSlotsAsync(Game game, CancellationToken ct);

    /// <summary>
    /// The slot to pre-select for a savegame: the one it used last if that is free, otherwise the first
    /// free one, otherwise null. Never changes the remembered slot.
    /// </summary>
    Task<SavegameSlotRef?> SuggestSlotAsync(Game game, Guid savegameId, CancellationToken ct);

    /// <summary>What one slot is to somebody about to write a savegame into it.</summary>
    /// <remarks>Hashes the slot only where a binding claims it, since only then does the content matter.</remarks>
    Task<SavegameSlotAvailability> ClassifySlotAsync(Game game, SavegameSlotRef slot, CancellationToken ct);

    /// <summary>
    /// What to call the savegame folder a hold is in, or null where the game has a single folder.
    /// A folder the settings no longer name is always named, by its key.
    /// </summary>
    string? DescribeFolder(Game game, TargetKey target);

    /// <summary>The number the player knows a slot by, or null for a game that does not number them.</summary>
    int? DescribeSlotNumber(Game game, SavegameSlotRef slot);

    /// <summary>
    /// What the adapter says about the save in a slot, in the shape the server stores it. Empty where the
    /// slot cannot be described; a failure here never fails the publish or check-in reading it.
    /// </summary>
    Task<List<SavegameDetailDto>> ReadDetailsAsync(
        ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, CancellationToken ct);
}
