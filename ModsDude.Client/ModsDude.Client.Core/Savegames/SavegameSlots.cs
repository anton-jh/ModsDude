using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameSlots(
    ILocalSavegameAdapters adapters,
    ISavegameBindingStore bindings,
    ISavegamePacker packer,
    ILogger<SavegameSlots> logger)
    : ISavegameSlots
{
    public async Task<IReadOnlyList<GameSavegameSlot>> GetSlotsAsync(Game game, CancellationToken ct)
    {
        var adapter = adapters.Require(game);
        var targets = adapter.SavegameTargets;
        var slots = new List<GameSavegameSlot>();

        foreach (var target in targets)
        {
            var name = TargetNames.Distinguishing(target.Key, target.DisplayName, targets.Count);

            foreach (var slot in await adapter.GetSlots(target, ct))
            {
                slots.Add(new GameSavegameSlot(new SavegameSlotRef(target.Key, slot.Id), name, slot));
            }
        }

        return slots;
    }

    public async Task<SavegameSlotRef?> SuggestSlotAsync(Game game, Guid savegameId, CancellationToken ct)
    {
        var slots = await GetSlotsAsync(game, ct);

        // Free-ness turns on the slot being empty and unclaimed, which no hash can change.
        bool IsFree(GameSavegameSlot slot)
            => SavegameSlotStates.Classify(slot, bindings.GetBindingForSlot(game.Identity, slot.Ref), null)
                is SavegameSlotAvailability.Free;

        if (bindings.GetSlotHint(game.Identity, savegameId) is SavegameSlotRef hint &&
            slots.FirstOrDefault(x => x.Ref.Addresses(hint)) is GameSavegameSlot remembered &&
            IsFree(remembered))
        {
            return remembered.Ref;
        }

        return slots.FirstOrDefault(IsFree)?.Ref;
    }

    public async Task<SavegameSlotAvailability> ClassifySlotAsync(Game game, SavegameSlotRef slotRef, CancellationToken ct)
    {
        var adapter = adapters.Require(game);
        var target = adapter.RequireTarget(game, slotRef);

        var slots = await adapter.GetSlots(target, ct);
        var slot = slots.FirstOrDefault(x => string.Equals(x.Id.Value, slotRef.Slot.Value, StringComparison.OrdinalIgnoreCase))
            // A slot the adapter does not list, for a game that can mint them: nothing is there to lose.
            ?? new SavegameSlot(slotRef.Slot, null, false, [], adapter.GetSlotNumber(slotRef.Slot));

        var addressed = new GameSavegameSlot(new SavegameSlotRef(target.Key, slot.Id), target.DisplayName, slot);
        var binding = bindings.GetBindingForSlot(game.Identity, addressed.Ref);

        var currentHash = binding is not null && slot.IsOccupied
            ? await packer.HashSlotAsync(adapter, target, slot.Id, ct)
            : null;

        return SavegameSlotStates.Classify(addressed, binding, currentHash);
    }

    public string? DescribeFolder(Game game, TargetKey target)
    {
        if (adapters.TryGet(game)?.SavegameTargets is not SavegameTargets targets)
        {
            return TargetNames.Of(target, null);
        }

        return targets[target] is SavegameTarget named
            ? TargetNames.Distinguishing(target, named.DisplayName, targets.Count)
            : TargetNames.Of(target, null);
    }

    public int? DescribeSlotNumber(Game game, SavegameSlotRef slot)
        => adapters.TryGet(game) is ILocalSavegameAdapter adapter ? adapter.GetSlotNumber(slot.Slot) : null;

    public async Task<List<SavegameDetailDto>> ReadDetailsAsync(
        ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, CancellationToken ct)
    {
        try
        {
            var slots = await adapter.GetSlots(target, ct);

            return [.. slots
                .FirstOrDefault(x => x.Id == slot)?.Details
                    .Select(x => new SavegameDetailDto { Key = x.Id, Label = x.Label, Value = x.Value })
                ?? []];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not describe slot {Slot}; the snapshot will carry no details.", slot.Value);

            return [];
        }
    }
}
