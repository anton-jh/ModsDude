using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Notices;

/// <summary>The repo a notice can act through, reduced to what deciding what to offer needs.</summary>
public sealed record NoticeRepo(Guid Id, RepoMembershipLevel MembershipLevel);


/// <summary>
/// Everything <see cref="NoticeBuilder"/> needs that is not in the drift results themselves.
/// </summary>
/// <remarks>
/// <para>
/// An interface for the reason <c>IDriftCandidateSource</c> and <c>IProfileRevisions</c> are ones:
/// the rule that decides what the column says is then a function of its inputs and can be exercised
/// without a signed-in client, a hydrated adapter or a shell. The drift check itself already works
/// that way, and the sentences it produces are the half that was never testable.
/// </para>
/// <para>
/// Every answer here is allowed to be absent, and absent is never an error. The notices are built
/// from persisted state so that they work offline and for a game no loaded repo serves, which is
/// precisely the case each of these returns null for.
/// </para>
/// </remarks>
public interface INoticeEnvironment
{
    /// <summary>
    /// Whether the repo list has been read at all.
    /// </summary>
    /// <remarks>
    /// An empty list means "not asked yet" for the second between the shell appearing and the first
    /// fetch landing, and "in no repos" ever after. Accusing a healthy startup of a lost membership
    /// would be the column inventing a state.
    /// </remarks>
    bool ReposLoaded { get; }

    /// <summary>
    /// The repo this game can be acted on through, or null where this account has none for it.
    /// </summary>
    /// <param name="profileRepoId">
    /// The repo of the profile the game follows, where it follows one. Preferred, because that is the
    /// one both mod actions address; an implementation falls back to any repo about the same game,
    /// which is enough to name folders and enough to tell a game this account can reach from one it
    /// cannot.
    /// </param>
    NoticeRepo? FindRepo(GameIdentity game, Guid? profileRepoId);

    /// <summary>
    /// What the adapter calls this game's folders. Empty is an ordinary answer - a game with one
    /// folder names none of them.
    /// </summary>
    IReadOnlyDictionary<TargetKey, string> FolderNames(GameIdentity game);

    /// <summary>
    /// Which revision a re-apply would install, where a held savegame pins one and that is not simply
    /// the profile's latest. Null where the apply is an ordinary follow-the-profile one.
    /// </summary>
    int? RequiredRevision(GameIdentity game, Guid profileId);
}
