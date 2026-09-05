using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// The three ways a savegame this machine is holding can have stopped agreeing with the server.
/// </summary>
/// <remarks>
/// <para>
/// Each is named for its <b>consequence</b> rather than for the condition that produced it, because
/// the condition is not what anybody needs to act on: "the recorded hash differs" is a fact about
/// two strings, and "you have an evening in slot 3 that nobody else can see" is a sentence somebody
/// can do something about. Same rule as the locked-mod drift one aggregate over.
/// </para>
/// <para>
/// They are not exclusive. A save can perfectly well have been played here <em>and</em> taken over
/// by somebody who checked in - which is the worst case and the one where saying only half of it
/// would be actively misleading - so the check returns every kind that applies.
/// </para>
/// </remarks>
public enum SavegameDriftKind
{
    /// <summary>
    /// The slot holds play newer than what was written into it. It exists nowhere but this disk
    /// until it is checked in.
    /// </summary>
    UncheckedInPlay,

    /// <summary>
    /// Somebody took the save over and checked in, so the version being held here is no longer the
    /// head. A check-in from here is a fork, and will be refused unless it is forced.
    /// </summary>
    TakenOverAndCheckedIn,

    /// <summary>
    /// The save was checked out against a mod list this folder is no longer on - either the profile
    /// moved, or the instance was applied to a different profile entirely. The case that corrupts
    /// saves, and the reason the locking exists at all.
    /// </summary>
    PlayedOnAnotherModList
}


/// <summary>
/// One drifted savegame in one instance, with the numbers the notice needs to say what happened.
/// </summary>
/// <param name="Slot">Where it is on this machine. Displayed by name, never by folder number.</param>
public sealed record SavegameDrift(
    Guid RepoId,
    Guid SavegameId,
    SavegameSlotId Slot,
    SavegameDriftKind Kind)
{
    /// <summary>What the game calls the save in that slot, where the adapter could read it.</summary>
    public string? SlotDisplayName { get; init; }

    /// <summary>The version this machine is holding - what a check-in from here would be based on.</summary>
    public int HeldVersion { get; init; }

    /// <summary>What the server's head is now, where the caller knew. Null is "not asked", not "unchanged".</summary>
    public int? HeadVersion { get; init; }

    /// <summary>The profile revision the save was checked out against.</summary>
    public int? PlayedRevision { get; init; }

    /// <summary>The revision the mod folder is actually on, from the sync manifest.</summary>
    public int? AppliedRevision { get; init; }
}


/// <summary>
/// Which version a savegame's head is at, for the savegames this client happens to know about.
/// </summary>
/// <remarks>
/// Deliberately partial, and deliberately <b>not</b> a client call - the same bargain
/// <see cref="Sync.IProfileRevisions"/> strikes for profiles, and for the same reason. The answer is
/// there for the repo whose savegame list the user has loaded and absent for the rest, because the
/// alternative is a network round trip per held savegame on every window activation, in a check
/// whose entire point is that it works offline and costs a directory listing.
/// </remarks>
public interface ISavegameHeadVersions
{
    /// <summary>The savegame's head version, or null where this client has not been told.</summary>
    int? GetHeadVersion(Guid repoId, Guid savegameId);
}


/// <summary>
/// Which of the three drift states a held savegame is in, decided from the binding, the slot's
/// current hash, the server's head and the revision the mod folder is on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure, and separate from the I/O for the same reason <see cref="SavegameSlotStates"/> is.</b>
/// Hashing the slot, reading the manifest and asking what the head is happen around this, never
/// inside it, so the rule that decides what the drift notice says is one function that can be
/// exercised exhaustively.
/// </para>
/// <para>
/// <b>It errs the opposite way to the slot safety check, on purpose.</b> An unknown - an unhashed
/// slot, a head nobody asked about - reports nothing rather than reporting drift. The asymmetry is
/// the cost of being wrong: the safety check is deciding whether to destroy an evening, where a
/// needless prompt is cheap; this one is deciding whether to raise a warning, and a warning that
/// fires when nothing is wrong is one everybody learns to click past - which is
/// docs/PLAN.md#phase-4--make-drift-unmissable read from the other end.
/// </para>
/// </remarks>
public static class SavegameDriftRules
{
    /// <param name="currentContentHash">
    /// The slot hashed now, or null where the caller did not hash it. Null says nothing, rather than
    /// saying the slot moved.
    /// </param>
    /// <param name="headVersion">The server's head, or null where nobody asked.</param>
    /// <param name="appliedProfileId">
    /// The profile the mod folder was last made to match. A <em>different</em> profile than the one
    /// the save was checked out against is the third state in its starkest form - the folder is not
    /// merely on another revision of the same list, it is on another list.
    /// </param>
    /// <param name="appliedRevision">Which revision of it, from the manifest.</param>
    public static IReadOnlyList<SavegameDriftKind> Classify(
        SavegameCheckoutBinding binding,
        string? currentContentHash,
        int? headVersion,
        Guid? appliedProfileId,
        int? appliedRevision)
    {
        var kinds = new List<SavegameDriftKind>();

        // Matches() rather than a string comparison, so a hash written by one part of the client and
        // compared by another cannot disagree over hex casing and report an evening that is not there.
        if (currentContentHash is not null && ModContentHasher.Matches(currentContentHash, binding.ContentHash) is false)
        {
            kinds.Add(SavegameDriftKind.UncheckedInPlay);
        }

        // Strictly past, not merely different. A client holding a head number older than the binding
        // is a client that has not refreshed, and inventing a takeover out of that would fire the
        // notice on stale data rather than on anything that happened.
        if (headVersion is int head && head > binding.Version)
        {
            kinds.Add(SavegameDriftKind.TakenOverAndCheckedIn);
        }

        if (HasMovedOffItsModList(binding, appliedProfileId, appliedRevision))
        {
            kinds.Add(SavegameDriftKind.PlayedOnAnotherModList);
        }

        return kinds;
    }


    /// <summary>
    /// Whether the folder this save is sitting in still runs the mod list it was checked out against.
    /// </summary>
    /// <remarks>
    /// <b>Costs no I/O at all</b>, which is why it is worth having: both numbers are already on disk
    /// in local state, so this one fires for a save the user has not touched, on a machine that is
    /// offline, before anything has been hashed.
    /// </remarks>
    private static bool HasMovedOffItsModList(
        SavegameCheckoutBinding binding,
        Guid? appliedProfileId,
        int? appliedRevision)
    {
        // A binding written before the revision was recorded, or a folder that has never been
        // synced, leaves the question unasked. Neither is evidence of anything.
        if (binding.ProfileRevision is not int played || appliedRevision is not int applied)
        {
            return false;
        }

        // Two revision numbers of two different profiles are not comparable at all: revision 6 of
        // 'Season 4' and revision 6 of 'Vanilla' are different mod lists that happen to share an
        // integer. A different profile is therefore drift on its own, without looking at the numbers.
        if (binding.ProfileId is Guid playedProfile && appliedProfileId is Guid appliedProfile)
        {
            return playedProfile != appliedProfile || played != applied;
        }

        return played != applied;
    }
}
