using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

public sealed class HeldSlotReader(
    ILocalSavegameAdapters adapters,
    ISavegameBindingStore bindings,
    ISavegamePacker packer,
    ILogger<HeldSlotReader> logger)
    : IHeldSlotReader
{
    public Task<IReadOnlyList<HeldSlotReading>> ReadAsync(GameIdentity game, CancellationToken ct)
        => ReadAsync(game, bindings.GetBindings(game), ct);

    public async Task<IReadOnlyList<HeldSlotReading>> ReadAsync(
        GameIdentity game, IReadOnlyList<SavegameCheckoutBinding> held, CancellationToken ct)
    {
        // The common case, nearly always: nothing held, so nothing is listed or hashed.
        if (held.Count == 0)
        {
            return [];
        }

        // A game no loaded repo serves reports nothing: unknown, not drifted.
        if (adapters.TryGet(game) is not ILocalSavegameAdapter adapter)
        {
            return [];
        }

        var slotsByTarget = new Dictionary<TargetKey, IReadOnlyList<GameSavegameSlot>>();
        var readings = new List<HeldSlotReading>();

        foreach (var binding in held)
        {
            ct.ThrowIfCancellationRequested();

            if (adapter.SavegameTargets[binding.Slot.Target] is not SavegameTarget savegameTarget)
            {
                continue;
            }

            if (slotsByTarget.TryGetValue(binding.Slot.Target, out var slots) is false)
            {
                slots = await ReadSlotsOrNothing(adapter, savegameTarget, ct);
                slotsByTarget[binding.Slot.Target] = slots;
            }

            var slot = slots.FirstOrDefault(x => x.Ref.Addresses(binding.Slot));

            // A slot deleted from inside the game has no contents to have moved, and hashing a missing
            // folder would report the empty archive as play.
            var currentHash = slot?.IsOccupied is true
                ? await HashOrNothing(adapter, savegameTarget, binding.Slot.Slot, ct)
                : null;

            readings.Add(new HeldSlotReading(binding, currentHash, slot?.DisplayName));
        }

        return readings;
    }

    private async Task<IReadOnlyList<GameSavegameSlot>> ReadSlotsOrNothing(
        ILocalSavegameAdapter adapter, SavegameTarget target, CancellationToken ct)
    {
        try
        {
            return [.. (await adapter.GetSlots(target, ct))
                .Select(x => new GameSavegameSlot(new SavegameSlotRef(target.Key, x.Id), target.DisplayName, x))];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not read the savegame slots in {Folder}; treating it as having none.", target.Path);

            return [];
        }
    }

    private async Task<string?> HashOrNothing(
        ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, CancellationToken ct)
    {
        try
        {
            return await packer.HashSlotAsync(adapter, target, slot, ct);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not hash slot {Slot}; reporting no drift for it.", slot.Value);

            return null;
        }
    }
}
