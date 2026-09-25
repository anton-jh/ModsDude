using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// What <see cref="FriendActivityRules"/> asks the running app, answered from the repositories the
/// shell already holds. Thin for the reason <see cref="NoticeEnvironment"/> is.
/// </summary>
public sealed class FriendActivityEnvironment(
    RepoRepository repoRepository,
    GameRepository gameRepository,
    IHeldSavegames heldSavegames)
    : IFriendActivityEnvironment
{
    /// <summary>
    /// A repo about the game names it best - that is the adapter's own name for it - and the game
    /// connected here is next best. The identity itself is the last resort, and only reachable for a
    /// game this machine knows nothing about.
    /// </summary>
    public string DescribeGame(string game)
    {
        if (FriendActivityRules.ParseGame(game) is not GameIdentity identity)
        {
            return game;
        }

        if (repoRepository.Repos.FirstOrDefault(x => x.Scope == identity) is Repo repo)
        {
            return repo.Adapter.GameDisplayName;
        }

        return gameRepository.Find(identity)?.Name ?? game;
    }

    public string? DescribeRepo(Guid repoId)
        => repoRepository.Repos.FirstOrDefault(x => x.Id == repoId)?.Name;

    public FollowAvailability CanFollow(GameActivityDto activity)
    {
        if (FriendActivityRules.ParseGame(activity.Game) is not GameIdentity identity)
        {
            return FollowAvailability.NotConnected;
        }

        return FriendActivityRules.CanFollow(
            gameRepository.Find(identity),
            heldSavegames.GetRequiredRevision(identity, activity.ProfileId),
            activity);
    }
}
