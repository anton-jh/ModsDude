using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Repos;

namespace ModsDude.Client.Core.GameProcesses;

/// <summary><see cref="IGameProcessNames"/> over the repos this client has loaded.</summary>
public sealed class RepoGameProcessNames(IRepoStore repos) : IGameProcessNames
{
    public IReadOnlyList<string> Get(GameIdentity game)
        => repos.Repos.FirstOrDefault(x => x.Scope == game)?.Adapter.ProcessNames ?? [];
}
