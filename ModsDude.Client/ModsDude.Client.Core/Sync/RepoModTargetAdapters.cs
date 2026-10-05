using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Sync;

/// <summary><see cref="IModTargetAdapters"/> over the repos this client has loaded and the games it has connected.</summary>
public sealed class RepoModTargetAdapters(IRepoRepository repos, IGameRepository games) : IModTargetAdapters
{
    public ResolvedModTarget? Find(ModTargetRef target)
    {
        var repo = repos.Repos.FirstOrDefault(x => x.Scope == target.Game);
        var game = games.Find(target.Game);

        if (repo is null || game is null)
        {
            return null;
        }

        if (game.GetAdapter(repo.Adapter).GetLocalCapabilityAdapterFactory<ILocalModAdapter>()?.Invoke() is not ILocalModAdapter adapter)
        {
            return null;
        }

        return adapter.ModTargets[target.Key] is ModTarget resolved
            ? new ResolvedModTarget(adapter, resolved)
            : null;
    }
}
