using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Notices;

/// <summary>
/// How loudly a notice is drawn, and where it sorts in the column.
/// </summary>
/// <remarks>
/// <para>
/// <b>Named for what is at stake, not for how the check found it.</b> The column is read top down by
/// somebody who has just come back from the game, and the only ordering that respects that is the
/// one that puts what can cost them an evening above what costs them a tidy folder.
/// </para>
/// <para>
/// The declaration order is the sort order - see <see cref="NoticeBuilder"/>, which sorts on the
/// enum itself so that a new severity cannot be added without deciding where it belongs.
/// </para>
/// </remarks>
public enum NoticeSeverity
{
    /// <summary>Something on this disk can be lost or damaged: a held savegame, a locked mod, the shared cache.</summary>
    Critical,

    /// <summary>The folder is not what it should be, and nothing is at risk while it waits.</summary>
    Warning,

    /// <summary>Work that was intended and has not happened. Nothing is wrong; something is owed.</summary>
    Pending,

    /// <summary>Something the app absorbed. Reported because it had a visible consequence.</summary>
    Info
}


/// <summary>
/// What a notice offers to do about itself.
/// </summary>
/// <remarks>
/// A kind rather than a command, because the notice model is in Core and every one of these is a
/// navigation or an apply that only the shell can perform. The shell binds them - see
/// <c>NoticeCenterViewModel</c> - and a notice that names one the shell cannot honour draws no
/// button rather than a dead one.
/// </remarks>
public enum NoticeActionKind
{
    /// <summary>Open the drifted profile's mod list, which is also where what the game downloaded gets imported.</summary>
    Review,

    /// <summary>Put the mod folder back onto its profile, in one click.</summary>
    Reapply,

    /// <summary>Open the repo's saves list with this savegame picked out, where check in and discard are.</summary>
    OpenSavegame,

    /// <summary>Open the log folder. The only thing the quiet notices have to offer.</summary>
    OpenLog,

    /// <summary>Restart into the version that has already been downloaded.</summary>
    RestartToUpdate,

    /// <summary>Stop waiting and try reaching the server again now.</summary>
    RetryConnection
}


/// <param name="Label">
/// What the button says, which is not always the same for one kind: a re-apply that is pinned to a
/// held savegame's revision names the number it is going to install.
/// </param>
public sealed record NoticeAction(NoticeActionKind Kind, string Label)
{
    /// <summary>
    /// Whether this is the one the user is most likely to want. At most one per notice, and it is
    /// drawn as the accent button.
    /// </summary>
    public bool IsPrimary { get; init; }
}


/// <summary>
/// What a notice is about, in the ids its actions need.
/// </summary>
/// <remarks>
/// Every field beyond the game is optional because the notices do not all have them: a corrupt
/// store blob belongs to a volume rather than to a game, and a game reported purely for a savegame
/// it is holding may follow no profile at all.
/// </remarks>
public sealed record NoticeSubject(GameIdentity Game, string GameName)
{
    /// <summary>Which of the game's folders, where the notice is about one of them.</summary>
    public ModTargetRef? Target { get; init; }

    public Guid? RepoId { get; init; }
    public Guid? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    public Guid? SavegameId { get; init; }
}


/// <summary>
/// One thing worth telling the user about, in the column on the right.
/// </summary>
/// <remarks>
/// <para>
/// <b>One notice is one problem with one remedy.</b> That is the whole of the model, and it is a
/// deliberate reversal: for several slices the shell drew a single drift card that multiplexed the
/// mod half, the locked half, the savegame half and the shared cache into one bordered box, and
/// closed the lists it could not fit with <em>2 more savegame problems here as well</em>. Those
/// tails were list items flattened into a sentence because there was only ever one card to put them
/// in. The column is the place that was missing, and the trailing counts go away by having somewhere
/// to be drawn.
/// </para>
/// <para>
/// <b>The old argument against this is on the record and is being overruled, not forgotten.</b> The
/// single card said that "two notices racing to say one each is how a warning becomes noise" and
/// that "a person acts on one problem at a time". Both are true of a corner with room for one card
/// in it. They stop being true of a list that sorts by what is at stake, collapses everything below
/// the fold, and groups a game's folders under the game - which is what this model exists to let the
/// shell do. If the column ever reads as a wall, the old shape was right.
/// </para>
/// <para>
/// <b>Keyed and signed, because dismissal is per notice now.</b> <see cref="Key"/> is what a
/// dismissal is filed under and is stable for as long as the problem is the same problem;
/// <see cref="Signature"/> is what the notice currently says, so that waving away two stray mods
/// does not also wave away the third one that arrives afterwards. See <see cref="DismissalLedger"/>.
/// </para>
/// </remarks>
/// <param name="Key">
/// Identity across rebuilds. The column rebuilds from scratch on every check, so this is what lets a
/// card keep its expanded state and its dismissal while the sentence inside it changes.
/// </param>
/// <param name="Signature">
/// Everything the notice currently says, reduced to a string. Dismissal is against this rather than
/// against a timestamp, so the same problem stays dismissed across re-checks and a changed one comes
/// straight back.
/// </param>
public sealed record Notice(
    string Key,
    string Signature,
    NoticeSeverity Severity,
    string Headline)
{
    /// <summary>The paragraph under the headline. Null where the headline is the whole of it.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// A quieter line under the body - the import prompt, and the log's whereabouts. Never carries
    /// anything the user has to read to understand the notice.
    /// </summary>
    public string? Footnote { get; init; }

    public IReadOnlyList<NoticeAction> Actions { get; init; } = [];

    /// <summary>
    /// Whether the user may wave this one away. False for the ones that report a state rather than an
    /// event - a repo this account cannot see does not stop being true because it was dismissed, and
    /// nothing else in the app mentions it.
    /// </summary>
    public bool CanDismiss { get; init; } = true;

    public NoticeSubject? Subject { get; init; }

    /// <summary>
    /// What the column draws as a heading above a run of notices, where several share it. Null for a
    /// notice that belongs to no group - the shared cache is about a drive, not about a game.
    /// </summary>
    /// <remarks>
    /// Grouping is the answer to the one real objection to a column: a machine running a dedicated
    /// server and an MP client can contribute three folder notices to one alt-tab, and three
    /// headlines each naming the same game reads as three games in trouble. Under one heading it
    /// reads as what it is.
    /// </remarks>
    public string? GroupLabel { get; init; }

    /// <summary>Who this notice can act through, where a membership level decides what it may offer.</summary>
    public RepoMembershipLevel? Membership { get; init; }
}
