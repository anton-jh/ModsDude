using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Persistence;

public class PersistedLocalInstance
{
    public required Guid Id { get; init; }

    /// <summary>The game this instance belongs to. Every repo with the same scope offers it.</summary>
    public required InstanceScope Scope { get; init; }

    /// <summary>
    /// Which adapter version authored <see cref="AdapterInstanceSettings"/>. Not part of the scope,
    /// so a repo on a newer compatibility version still offers this instance and has to be able to
    /// read the older settings.
    /// </summary>
    public required GameAdapterId GameAdapterId { get; init; }

    public required string Name { get; set; }
    public required string AdapterInstanceSettings { get; set; }

    /// <summary>
    /// The folder the adapter says this instance owns, recorded so the ownership check can run
    /// across every scope. An instance whose scope has no repo on this machine cannot hydrate its
    /// adapter, and it still owns its folder.
    /// </summary>
    public string? ModFolder { get; set; }

    public ActiveProfile? ActiveProfile { get; set; }

    /// <summary>
    /// The savegames this instance currently holds, one per occupied slot. Underivable once somebody
    /// has played, so it is persisted rather than worked out - see
    /// <see cref="SavegameCheckoutBinding"/>.
    /// </summary>
    /// <remarks>
    /// A list rather than a single entry because a game has several slots, but a short one: a slot
    /// is held only while a save is checked out. At most one entry per savegame and one per slot,
    /// which is what makes a check-in able to act without asking anything.
    /// </remarks>
    public List<SavegameCheckoutBinding> SavegameCheckouts { get; init; } = [];

    /// <summary>
    /// Where each savegame was last put on this machine. Kept after a check-in, purely to pre-select
    /// the picker next time, and discarded silently whenever it turns out to be wrong.
    /// </summary>
    public List<SavegameSlotHint> SavegameSlotHints { get; init; } = [];
}
