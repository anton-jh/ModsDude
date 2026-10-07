using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Repos;

namespace ModsDude.Client.Core.Savegames;

public sealed class RepoSavegameAdapters(IRepoStore repos, IGameRepository games)
    : ILocalSavegameAdapters
{
    public ILocalSavegameAdapter? TryGet(GameIdentity identity)
        => games.Find(identity) is Game game
            ? TryGet(game)
            : null;

    public ILocalSavegameAdapter? TryGet(Game game)
    {
        // Any repo serving the identity will do: the settings that differ between two repos on one
        // game are the mod catalogue's, and a savegame adapter reads none of them.
        foreach (var repo in repos.Repos.Where(x => x.Scope == game.Identity))
        {
            if (repo.Adapter.CanSupportSavegames is false)
            {
                continue;
            }

            if (game.GetAdapter(repo.Adapter).GetLocalCapabilityAdapterFactory<ILocalSavegameAdapter>() is Func<ILocalSavegameAdapter> factory)
            {
                return factory();
            }
        }

        return null;
    }
}
