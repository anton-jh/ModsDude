using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Persistence;

/// <summary>
/// One game on this machine, as it is written down.
/// </summary>
/// <remarks>
/// <b>No id.</b> It is keyed by its <see cref="GameIdentity"/> in <see cref="LocalState.Games"/>, so
/// the same game configured twice is not a state that can be written rather than one something has to
/// check for. Everything that would have keyed on a Guid - the manifest, the savegame bindings, the
/// mod source - keys on the identity, which is also what ends up in the manifest's filename.
/// </remarks>
public class PersistedGame
{
    /// <summary>
    /// Which adapter version authored <see cref="AdapterLocalSettings"/>. Not part of the identity,
    /// so a repo on a newer compatibility version still offers this game and has to be able to
    /// read the older settings.
    /// </summary>
    public required GameAdapterId GameAdapterId { get; init; }

    public required string Name { get; set; }
    public required string AdapterLocalSettings { get; set; }

    /// <summary>
    /// Every target the adapter says this game reaches, rewritten whenever the settings are.
    /// </summary>
    /// <remarks>
    /// <b>Derived and still persisted, which is not redundancy.</b> Store eviction and the drift
    /// candidate list both need a folder path <em>without hydrating an adapter</em>: a game whose
    /// identity no loaded repo serves still owns its folders and still has a standing intent, and in
    /// that state its <see cref="AdapterLocalSettings"/> are an opaque blob. Empty is an ordinary
    /// answer - a game whose settings point at no folder at all.
    /// </remarks>
    public List<PersistedModTarget> Targets { get; set; } = [];

    public ActiveProfile? ActiveProfile { get; set; }

    /// <summary>
    /// The savegames this game currently holds, one per occupied slot. Underivable once somebody
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


/// <summary>
/// One of a game's targets as it is written down: the adapter's key for it, and the folder it
/// reached when the settings were last saved.
/// </summary>
/// <remarks>
/// <b>The key is the half that earns this being a record rather than a string.</b> A manifest and an
/// eviction pin are filed under <see cref="ModTargetRef"/>, so reading a folder path without
/// hydrating an adapter is only useful if the key beside it says which manifest the folder's contents
/// are described by. <see cref="ModTarget.DisplayName"/> is deliberately not here: it is what to call
/// the folder on screen, which needs the adapter that named it and is re-derived whenever there is
/// one.
/// </remarks>
public sealed record PersistedModTarget(TargetKey Key, string ModFolder);
