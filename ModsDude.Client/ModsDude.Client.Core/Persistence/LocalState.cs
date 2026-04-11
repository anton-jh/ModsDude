using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Persistence;

public class LocalState
{
    public int Version { get; set; } = 1;
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
