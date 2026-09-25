using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Persistence;

public class LocalState
{
    /// <summary>
    /// Bumped whenever the persisted shape changes. There is no migration: state written by an
    /// older version is discarded by <see cref="StateStore"/>'s compatibility check, which is
    /// affordable while the system has no users.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bumped to 7 because <see cref="PersistedGame.Name"/> stopped being something somebody typed
    /// and became what the adapter calls the game. It is not a parse error on the way in - a version
    /// 6 state reads back with whatever the user called it - and that is exactly why it is a bump
    /// rather than a shrug: a game left calling itself 'Game' forever, because the field is derived
    /// now and nothing routine rewrites it, is this machine quietly disagreeing with what the code
    /// means. Reconnecting the game is the whole cost.
    /// </para>
    /// <para>
    /// Bumped to 6 because a savegame checkout and its slot hint carry a
    /// <see cref="Models.SavegameSlotRef"/> rather than a bare slot id: a slot is a place in one of
    /// the game's targets now, and "savegame3" names one on every folder a game reaches. A version 5
    /// binding has a string where a reference is expected, which is a parse error rather than the
    /// deliberate discard this is - and read, it would hold a slot in whichever folder happened to be
    /// first. Local state is what this costs: the active profile, the holds and the slot hints go,
    /// and a savegame checked out on the server has to be disconnected and taken again.
    /// </para>
    /// <para>
    /// Bumped to 5 because <see cref="PersistedGame.Targets"/> carries a
    /// <see cref="TargetKey"/> beside each folder. A version 4 state holds bare path strings there,
    /// and a string where an object is expected is a parse error rather than the deliberate discard
    /// this is - but even read, a folder with no key names no manifest, which is the one thing the
    /// list is persisted for. The <see cref="Models.ModSourceId"/> format changed again with it and
    /// the "do not look in this folder" preferences go again; that is the second time, and it is
    /// still cheaper than a migration for a system with no users.
    /// </para>
    /// <para>
    /// Bumped to 4 because <see cref="Games"/> is keyed by <see cref="GameIdentity"/> now, and a
    /// <see cref="PersistedGame"/> has lost its id and its scope to that key and grown an <c>s</c> on
    /// its mod folder. A version 3 state would read into this shape as an <em>empty</em> dictionary -
    /// no game configured anywhere, and so no profile active anywhere - which is the one thing that
    /// must not be silently guessed. The <see cref="Models.ModSourceId"/> format changed with it, so
    /// the "do not look in this folder" preferences go too; harmless, and it rides this bump rather
    /// than earning one of its own.
    /// </para>
    /// <para>
    /// Bumped to 3 because <c>AdapterInstanceSettings</c> was renamed to
    /// <see cref="PersistedGame.AdapterLocalSettings"/>. A version 2 state has the old
    /// property name, and the settings are a required member, so reading one would fail as a parse
    /// error rather than as the deliberate discard it is. The rename is Phase 10 retiring the word
    /// instance from the adapter layer; the state is cheap, and carrying a second spelling to avoid
    /// paying for it is how a schema grows a history nobody asked for.
    /// </para>
    /// </remarks>
    public const int CurrentVersion = 7;


    public int Version { get; set; } = CurrentVersion;
    public List<Guid> LastSelectedRepos { get; init; } = [];
    public List<Guid> LastSelectedProfiles { get; init; } = [];
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
