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
    /// Bumped to 3 because <c>AdapterInstanceSettings</c> was renamed to
    /// <see cref="PersistedLocalInstance.AdapterLocalSettings"/>. A version 2 state has the old
    /// property name, and the settings are a required member, so reading one would fail as a parse
    /// error rather than as the deliberate discard it is. The rename is Phase 10 retiring the word
    /// instance from the adapter layer; the state is cheap, and carrying a second spelling to avoid
    /// paying for it is how a schema grows a history nobody asked for.
    /// </para>
    /// <para>
    /// Not bumped for the savegame collections on <see cref="PersistedLocalInstance"/>. A version 2
    /// state deserializes with both of them empty, which reads as "this machine holds no savegame" -
    /// true, and the right answer. Bumping would throw away every configured instance to learn
    /// something already known.
    /// </para>
    /// </remarks>
    public const int CurrentVersion = 3;


    public int Version { get; set; } = CurrentVersion;
    public List<Guid> LastSelectedRepos { get; init; } = [];
    public List<Guid> LastSelectedProfiles { get; init; } = [];
    public ClientSettings Settings { get; init; } = new();

    /// <summary>
    /// Instances are keyed by their own id and scoped to a game, not owned by a repo: one game
    /// installation is configured once and offered under every repo targeting that game.
    /// </summary>
    public Dictionary<Guid, PersistedLocalInstance> Instances { get; init; } = [];
}
