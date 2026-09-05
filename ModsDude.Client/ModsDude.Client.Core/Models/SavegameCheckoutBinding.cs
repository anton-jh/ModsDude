namespace ModsDude.Client.Core.Models;

/// <summary>
/// A savegame this machine currently holds, and the slot it was written into.
/// </summary>
/// <remarks>
/// <para>
/// <b>A source of truth, not a cache</b>, for the same reason <see cref="ActiveProfile"/> is. Once
/// somebody has played, the bytes in the slot match no version on the server, so nothing can work
/// out afterwards which savegame that slot was. Losing this loses the ability to check the save back
/// in at all.
/// </para>
/// <para>
/// It exists only while the save is checked out. Checking in frees the slot, which is what removes
/// any need to evict anything: the slots ModsDude occupies are the saves somebody is actually
/// playing, which is one or two rather than twenty.
/// </para>
/// </remarks>
/// <param name="Version">The version that was written into the slot - what a check-in is based on.</param>
/// <param name="ContentHash">
/// What was written, so that the slot having moved since is a comparison rather than a guess. This
/// is the half that could be recomputed by rehashing the slot, and the only reason it is stored is
/// to make the check cheap.
/// </param>
public readonly record struct SavegameCheckoutBinding(
    Guid RepoId,
    Guid SavegameId,
    string SlotId,
    int Version,
    string ContentHash,
    DateTime WrittenAt)
{
    /// <summary>
    /// The profile the version being held was played on, and which revision of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded here because it is the only place it can be: the version's own revision lives on the
    /// server, and asking for it is a network call in a drift check that must work offline. With both
    /// numbers in local state, "this save was checked out against a mod list this folder no longer
    /// runs" - the state that actually corrupts saves - costs no I/O whatsoever.
    /// </para>
    /// <para>
    /// Nullable, and appended as properties rather than as positional members: a binding persisted
    /// before this existed deserializes with both null, which reads as "not recorded" and leaves the
    /// question unasked. Discarding those bindings instead would lose the ability to check the saves
    /// they name back in at all, which is a far worse trade than one drift state going unreported.
    /// </para>
    /// </remarks>
    public Guid? ProfileId { get; init; }

    /// <inheritdoc cref="ProfileId"/>
    public int? ProfileRevision { get; init; }
}


/// <summary>
/// Where a savegame was last put on this machine, remembered so the picker can pre-select it.
/// </summary>
/// <remarks>
/// <b>Advisory, and worth nothing when wrong.</b> Unlike <see cref="SavegameCheckoutBinding"/> this
/// is never repaired and never trusted - the slot it names may since have been filled by something
/// else, in which case the picker says so and offers the first free one instead. It survives a
/// check-in precisely because its whole job is the next check-out.
/// </remarks>
public readonly record struct SavegameSlotHint(
    Guid RepoId,
    Guid SavegameId,
    string SlotId);
