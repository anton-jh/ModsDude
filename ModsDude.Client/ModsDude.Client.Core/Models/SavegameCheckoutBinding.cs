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
/// What was written at check-out, so that the slot having moved since is a comparison rather than a
/// guess. This is the half that could be recomputed by rehashing the slot, and the only reason it is
/// stored is to make the check cheap.
/// </param>
public readonly record struct SavegameCheckoutBinding(
    Guid RepoId,
    Guid SavegameId,
    string SlotId,
    int Version,
    string ContentHash,
    DateTime WrittenAt)
{
    private readonly string? _lastObservedHash;


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
    /// <b>Both set or both null</b>, which is the constraint the server rows carry too. A savegame
    /// following no mod list has neither: it records no revision, is in no succession, and takes no
    /// part in play attribution. A revision without the profile it belongs to is not a number
    /// anything can compare - revision 6 of two different lists is one integer and two mod lists.
    /// </para>
    /// </remarks>
    public Guid? ProfileId { get; init; }

    /// <inheritdoc cref="ProfileId"/>
    public int? ProfileRevision { get; init; }

    /// <summary>
    /// The slot's bytes when they were last examined - the boundary "has this been played since we
    /// last looked?" is measured from.
    /// </summary>
    /// <remarks>
    /// <b>Not <see cref="ContentHash"/>, and the two must not be collapsed into one field.</b> They
    /// answer different questions and have different lifetimes: <see cref="ContentHash"/> is what the
    /// server holds and never moves while the save is held, so the drift notice compares against it
    /// to report play that exists on this disk and nowhere else; this one is rewritten at every
    /// observation. One field would compare equal to itself the moment an apply refreshed it, and
    /// unchecked-in play would silently stop being reported. See
    /// docs/10-savegame-profile-binding.md#two-hashes-two-questions.
    /// </remarks>
    /// <value>
    /// Unset reads as <see cref="ContentHash"/>, because the bytes written at check-out are exactly
    /// what the first observation has to compare against. Every way of taking a binding says so
    /// anyway; this is what makes it impossible for a new one to leave the boundary unrecorded and
    /// report a fresh check-out as an evening.
    /// </value>
    public string LastObservedHash
    {
        get => _lastObservedHash ?? ContentHash;
        init => _lastObservedHash = value;
    }

    /// <summary>
    /// The revision of <see cref="ProfileId"/> this savegame runs on, or null where it follows
    /// whatever the profile's head is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A number here is the whole of "this instance is holding a past savegame".</b> Stored rather
    /// than inferred from revision numbers, because inferring it needs the server's answer to "is this
    /// still its profile's current farm?" and the two things that read it - the apply table and the
    /// drift check - both have to work offline and cost a directory listing.
    /// </para>
    /// <para>
    /// <b>Not <see cref="ProfileRevision"/>, which it happens to equal at check-out.</b> That one is
    /// what the held version was <em>played</em> on and belongs to play attribution; this is what the
    /// mod folder has to be on and belongs to the rules. A current savegame's are already different -
    /// it was played on some older revision and runs on head - and nothing that decides drift reads
    /// the other.
    /// </para>
    /// </remarks>
    public int? TargetRevision { get; init; }

    /// <summary>
    /// The newest profile revision play has actually been observed on, or null until any has been.
    /// </summary>
    /// <remarks>
    /// What a check-in records the version as played on, in preference to whatever the folder happens
    /// to be on when the save is handed back: an apply between the last evening and the check-in moves
    /// the folder and not the play, and the interval between the two does not enter into it. Null is
    /// the never-played case and falls back to the folder's revision, where the slot's bytes still
    /// equal the held version's and the server mints nothing anyway.
    /// </remarks>
    public int? LastPlayedRevision { get; init; }
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
