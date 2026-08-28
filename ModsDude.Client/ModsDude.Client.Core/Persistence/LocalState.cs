using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Persistence;

public class LocalState
{
    /// <summary>
    /// Bumped whenever the persisted shape changes. There is no migration: state written by an
    /// older version is discarded by <see cref="StateStore"/>'s compatibility check, which is
    /// affordable while the system has no users.
    /// </summary>
    public const int CurrentVersion = 1;


    public int Version { get; set; } = CurrentVersion;
    public List<Guid> LastSelectedRepos { get; init; } = [];
    public List<Guid> LastSelectedProfiles { get; init; } = [];
    public Dictionary<Guid, LocalRepoState> Repos { get; init; } = [];


    public LocalRepoState GetRepoStateById(Guid repoId)
    {
        if (!Repos.TryGetValue(repoId, out var value))
        {
            value = new LocalRepoState();
            Repos[repoId] = value;
        }

        return value;
    }
}

public class LocalRepoState
{
    public ObservableCollection<PersistedLocalInstance> LocalInstances { get; set; } = [];
}
