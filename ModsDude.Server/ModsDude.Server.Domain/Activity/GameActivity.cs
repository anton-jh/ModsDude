using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Activity;

/// <summary>
/// Which profile one person's game is on, as their client last said - so the people they share a
/// repo with can see it, and follow them onto it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A current state, not a log.</b> One row per person per game, overwritten by every report. What
/// anybody reading it wants is "what is Alex on now", and a history would have to be folded back into
/// that on every read and pruned besides. Somebody playing from two machines has one row, and the
/// machine that spoke last is the one it describes.
/// </para>
/// <para>
/// <b>Reported, because only the client knows.</b> Which profile a game follows is local intent - see
/// the client's <c>ActiveProfile</c> - and so is which game a repo is about: the game identity is
/// worked out from the adapter's settings by adapter code the server does not have. So the client
/// sends the identity along, and <see cref="Game"/> is what groups one person's repos into one game.
/// </para>
/// <para>
/// <b>Two times, because two questions are asked of it.</b> <see cref="TouchedAt"/> moves on every
/// report, re-applying included, and orders the lists - who has been playing lately.
/// <see cref="ChangedAt"/> moves only when something a friend would want to hear about happened -
/// another profile, a savegame checked out - and is what a client announces.
/// </para>
/// <para>
/// <b>Playing is a heartbeat.</b> The client says so every minute while the game runs, and once more
/// when it closes. A client that crashes or goes offline says nothing, so a game counts as playing
/// only until <see cref="PlayingTimeout"/> after the last beat.
/// </para>
/// <para>
/// There is no deactivated state. A game that stops following a profile is on nothing, and the row is
/// deleted: a list of what people are on has nothing to say about it.
/// </para>
/// </remarks>
public class GameActivity
{
    /// <summary>How long after the last heartbeat a game still counts as playing: a few missed beats.</summary>
    public static readonly TimeSpan PlayingTimeout = TimeSpan.FromMinutes(3);


    // ef
    private GameActivity() { }

    public GameActivity(
        UserId userId,
        GameKey game,
        RepoId repoId,
        ProfileId profileId,
        RevisionNumber? pinnedRevision,
        GameActivityKind kind,
        SavegameId? savegameId,
        DateTime now)
    {
        UserId = userId;
        Game = game;
        RepoId = repoId;
        ProfileId = profileId;
        PinnedRevision = pinnedRevision;
        Kind = kind is GameActivityKind.Reapplied ? GameActivityKind.Activated : kind;
        SavegameId = savegameId;
        ChangedAt = now;
        TouchedAt = now;
    }


    public UserId UserId { get; private set; }

    /// <summary>Which game, as the client identifies it. Together with <see cref="UserId"/>, the key.</summary>
    public GameKey Game { get; private set; }

    public RepoId RepoId { get; private set; }
    public ProfileId ProfileId { get; private set; }

    /// <summary>
    /// The revision the game is held to, or null - nearly always - where it follows the profile's head.
    /// </summary>
    /// <remarks>
    /// Only ever set where something chose a revision: a savegame checked out in compatibility mode, or somebody
    /// following a friend who had one. An ordinary activation is on head by definition, and writing
    /// down the number head happened to be would make a follower pin themselves to it.
    /// </remarks>
    public RevisionNumber? PinnedRevision { get; private set; }

    /// <summary>
    /// What <see cref="ChangedAt"/> was. Never <see cref="GameActivityKind.Reapplied"/>: a re-apply
    /// is not a change, so it leaves the last one standing.
    /// </summary>
    public GameActivityKind Kind { get; private set; }

    /// <summary>The savegame checked out, where <see cref="Kind"/> is a check-out.</summary>
    public SavegameId? SavegameId { get; private set; }

    /// <summary>When this game last moved onto something - another profile, or a savegame.</summary>
    public DateTime ChangedAt { get; private set; }

    /// <summary>When this game was last reported at all, re-applies and heartbeats included.</summary>
    public DateTime TouchedAt { get; private set; }

    /// <summary>When the current play session started, or null where the game is not being played.</summary>
    public DateTime? PlayingSince { get; private set; }

    /// <summary>The last heartbeat of the current play session, or null where the game is not being played.</summary>
    public DateTime? PlayingSeenAt { get; private set; }

    /// <summary>Until when the game counts as playing without another heartbeat, or null where it is not being played.</summary>
    public DateTime? PlayingUntil => PlayingSeenAt + PlayingTimeout;


    public bool IsPlaying(DateTime now) => PlayingUntil is DateTime until && now < until;

    /// <summary>
    /// Folds in a heartbeat. A session starts where the game was not playing - never, stopped, or
    /// timed out - and goes on otherwise.
    /// </summary>
    public void SeenPlaying(DateTime now)
    {
        if (IsPlaying(now) is false)
        {
            PlayingSince = now;
        }

        PlayingSeenAt = now;
        TouchedAt = now;
    }

    public void StoppedPlaying()
    {
        PlayingSince = null;
        PlayingSeenAt = null;
    }


    /// <summary>
    /// Folds one report in.
    /// </summary>
    /// <remarks>
    /// <b>A re-apply of something else is a change.</b> The client reports a re-apply only where the
    /// game was already on the profile, so a re-apply naming another one means a report went missing
    /// in between - and the people reading this have not heard about the switch either.
    /// </remarks>
    public void Record(
        RepoId repoId,
        ProfileId profileId,
        RevisionNumber? pinnedRevision,
        GameActivityKind kind,
        SavegameId? savegameId,
        DateTime now)
    {
        var sameProfile = repoId == RepoId && profileId == ProfileId;

        TouchedAt = now;
        PinnedRevision = pinnedRevision;

        if (kind is GameActivityKind.Reapplied && sameProfile)
        {
            return;
        }

        RepoId = repoId;
        ProfileId = profileId;
        Kind = kind is GameActivityKind.Reapplied ? GameActivityKind.Activated : kind;
        SavegameId = kind is GameActivityKind.SavegameCheckedOut ? savegameId : null;
        ChangedAt = now;
    }
}


/// <summary>What a report says happened.</summary>
public enum GameActivityKind
{
    /// <summary>The game moved onto the profile.</summary>
    Activated,

    /// <summary>The game was put back onto the profile it already follows. Not news to anybody.</summary>
    Reapplied,

    /// <summary>A savegame following the profile was checked out, which moves the game onto it.</summary>
    SavegameCheckedOut
}


/// <summary>
/// A game as the client identifies it - an adapter id and, where one adapter serves several games, a
/// discriminator. Opaque here: the server compares it and never takes it apart.
/// </summary>
public readonly record struct GameKey
{
    public const int MaximumLength = 200;


    public GameKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("A game must be named.");
        }

        if (value.Length > MaximumLength)
        {
            throw new DomainValidationException($"A game identity cannot be longer than {MaximumLength} characters.");
        }

        Value = value;
    }


    public string Value { get; }

    public override string ToString() => Value;
}
