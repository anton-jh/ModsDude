using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Persistence;

public class LocalState
{
    /// <summary>
    /// Bumped whenever the persisted shape changes in a way an older state would be misread by. There
    /// is no migration: state written by an older version is discarded by <see cref="StateStore"/>'s
    /// compatibility check. A removed property needs no bump, since reading ignores it.
    /// </summary>
    public const int CurrentVersion = 7;


    public int Version { get; set; } = CurrentVersion;
    public ClientSettings Settings { get; init; } = new();

    /// <summary>
    /// The newest friend activity each account has already been told about, by user id - so starting
    /// the app announces what happened while it was closed, and not the whole of last week again.
    /// </summary>
    /// <remarks>
    /// Server time, as the server stamped the activity, rather than this machine's clock: the two are
    /// only ever compared with each other, and a clock running fast here would otherwise swallow news.
    /// An additive field, so it is not a version bump - a state without it reads as an empty map.
    /// </remarks>
    public Dictionary<string, DateTime> FriendActivitySeenUntil { get; init; } = [];

    /// <summary>
    /// The games on this machine, keyed by which game each one is an installation of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed by identity rather than by an id of its own, so the same game configured twice stops
    /// being representable rather than being checked for. A game is not owned by a repo - it is
    /// configured once and offered under every repo targeting that game.
    /// </para>
    /// <para>
    /// A record struct as a dictionary key needs
    /// <see cref="GameIdentityJsonConverter.WriteAsPropertyName"/>, or it serializes as an object and
    /// the whole file stops round-tripping.
    /// </para>
    /// </remarks>
    public Dictionary<GameIdentity, PersistedGame> Games { get; init; } = [];
}
