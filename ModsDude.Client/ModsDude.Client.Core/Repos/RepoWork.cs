using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Repos;

/// <inheritdoc cref="IRepoWork"/>
public sealed class RepoWork(
    IResourceLeases leases,
    IProfileStore profiles,
    ISavegameStore savegames,
    IHeldSavegameClaims held,
    IGameRepository games)
    : IRepoWork
{
    public IResourceLease? TryClaim(Guid repoId, string holder)
        => leases.TryAcquireExclusive(KeysOf(repoId), holder);

    public string? DescribeBusy(Guid repoId)
        => KeysOf(repoId).Select(leases.DescribeHolder).FirstOrDefault(x => x is not null);


    private IReadOnlyList<string> KeysOf(Guid repoId)
        => KeysOf(
            repoId,
            profiles.Live(repoId).Select(x => x.Id),
            savegames.Live(repoId).Select(x => x.Id).Concat(held.Capture().Where(x => x.RepoId == repoId).Select(x => x.SavegameId)),
            games.Games);

    /// <summary>
    /// The repo's own mod set, each of its profiles, each of its savegames, and the mod folders of every
    /// game following one of its profiles.
    /// </summary>
    internal static IReadOnlyList<string> KeysOf(
        Guid repoId,
        IEnumerable<Guid> profileIds,
        IEnumerable<Guid> savegameIds,
        IEnumerable<Game> games)
        => [
            ResourceKeys.Repo(repoId),
            .. profileIds.Select(ResourceKeys.Profile),
            .. savegameIds.Select(ResourceKeys.Savegame),
            .. games
                .Where(x => x.ActiveProfile?.RepoId == repoId)
                .SelectMany(x => x.TargetRefs)
                .Select(ResourceKeys.Target)
        ];
}
