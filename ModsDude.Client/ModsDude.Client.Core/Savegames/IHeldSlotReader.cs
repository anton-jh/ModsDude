using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Hashes the slots this machine holds savegames in, as they are now.</summary>
/// <remarks>
/// A hold whose folder the settings no longer name is left out: there is nothing to hash. A slot
/// that is empty or cannot be read comes back with no hash, which reads as "nothing moved".
/// </remarks>
public interface IHeldSlotReader
{
    /// <summary>Every savegame this game holds, with its slot hashed now.</summary>
    Task<IReadOnlyList<HeldSlotReading>> ReadAsync(GameIdentity game, CancellationToken ct);

    /// <summary>The given holds of this game, with their slots hashed now.</summary>
    Task<IReadOnlyList<HeldSlotReading>> ReadAsync(
        GameIdentity game, IReadOnlyList<SavegameCheckoutBinding> held, CancellationToken ct);
}


/// <summary>One held savegame's slot, as it was read just now.</summary>
/// <param name="CurrentHash">The slot's bytes hashed, or null where it is empty or could not be read.</param>
/// <param name="SlotDisplayName">What the game calls the save in that slot, where it could be read.</param>
public sealed record HeldSlotReading(
    SavegameCheckoutBinding Binding,
    string? CurrentHash,
    string? SlotDisplayName);
