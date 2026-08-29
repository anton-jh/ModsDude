namespace ModsDude.Client.Core.Persistence;

public class LocalState
{
    /// <summary>
    /// Bumped whenever the persisted shape changes. There is no migration: state written by an
    /// older version is discarded by <see cref="StateStore"/>'s compatibility check, which is
    /// affordable while the system has no users.
    /// </summary>
    public const int CurrentVersion = 2;


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
