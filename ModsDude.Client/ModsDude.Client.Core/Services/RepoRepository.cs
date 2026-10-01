using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;
public class RepoRepository(
    IReposClient repoClient,
    IGameAdapterIndex gameAdapterIndex,
    IGameRepository gameRepository,
    ILogger<RepoRepository> logger)
    : IRepoRepository
{
    public event Action<Guid>? RepoCreated;

    public ObservableCollection<Repo> Repos { get; } = [];

    public bool HasLoaded { get; private set; }

    public RemoteChanges? PendingChanges { get; private set; }

    public event EventHandler? PendingChangesChanged;


    public async Task CheckForChanges(CancellationToken cancellationToken)
    {
        // Nothing to compare against yet, and before sign-in there is nobody to ask for.
        if (HasLoaded is false)
        {
            return;
        }

        var before = Snapshot();
        var reposFromApi = await repoClient.GetMyReposV1Async(cancellationToken);

        if (HasLoaded is false || before.SequenceEqual(Snapshot()) is false)
        {
            return;
        }

        SetPendingChanges(RepoListChanges.Between(before, reposFromApi));
    }

    public async Task RefreshRepos(CancellationToken cancellationToken)
    {
        var reposFromApi = await repoClient.GetMyReposV1Async(cancellationToken);

        var byId = reposFromApi.ToDictionary(x => x.Repo.Id);

        // Reconciled rather than rebuilt. Clearing would discard every menu entry and every open
        // page built from these repos, and each Repo holds a synchronizer subscribed to the
        // machine's game list that has to be disposed exactly when the repo really goes away.
        for (var i = Repos.Count - 1; i >= 0; i--)
        {
            if (!byId.ContainsKey(Repos[i].Id))
            {
                Remove(Repos[i]);
            }
        }

        foreach (var dto in reposFromApi)
        {
            if (FindRepo(dto.Repo.Id) is Repo existing)
            {
                existing.Apply(dto);
            }
            else
            {
                Repos.Add(MapRepoModel(dto));
            }
        }

        foreach (var repo in Repos)
        {
            CatchUpGame(repo);
        }

        // Last, so that a listener woken by the collection changing above sees the list before it
        // sees the flag saying the list is complete.
        HasLoaded = true;

        SetPendingChanges(null);
    }

    /// <summary>
    /// Brings the game a repo is about up to date with this machine: connects it where it
    /// <see cref="GameRepository.ConnectsAutomatically">connects automatically</see> and has turned up
    /// since the last look, and otherwise catches its folders up with wherever they have moved - see
    /// <see cref="GameRepository.RefreshTargets"/>. Here because this is the first moment there are
    /// adapters to ask, and it comes round again on every refresh.
    /// </summary>
    /// <remarks>
    /// Best-effort: a game that cannot be connected or asked right now stays as it was. Not being
    /// installed is the ordinary reason, and the repo's Overview says so; anything else is logged.
    /// </remarks>
    private void CatchUpGame(Repo repo)
    {
        try
        {
            if (gameRepository.Find(repo.Scope) is Game game)
            {
                gameRepository.RefreshTargets(game, repo.Adapter);
            }
            else
            {
                gameRepository.ConnectAutomatically(repo.Adapter);
            }
        }
        catch (UserFriendlyException exception)
        {
            logger.LogInformation("Left the game of repo {Repo} as it was: {Reason}", repo.Id, exception.DeveloperMessage);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not bring the game of repo {Repo} up to date with this machine.", repo.Id);
        }
    }

    public async Task CreateRepo(string name, string adapterId, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        var request = new CreateRepoRequest()
        {
            Name = name,
            AdapterId = adapterId,
            AdapterConfiguration = baseSettings.Serialize(),
        };

        // No name to lose the race for: repo names are not unique, so the only reason this could
        // come back a failure is one the error reporter can say better than a catch here.
        var repo = await repoClient.CreateRepoV1Async(request, cancellationToken);

        // The creator is the repo's first Admin, so the response carries everything the list needs.
        var created = MapRepoModel(new RepoMembershipDto()
        {
            Repo = repo,
            MembershipLevel = RepoMembershipLevel.Admin
        });

        Repos.Add(created);

        // Before the shell navigates to it, so it opens with its game already there.
        CatchUpGame(created);

        RepoCreated?.Invoke(repo.Id);
    }

    public void AddJoinedRepo(RepoMembershipDto membership)
    {
        if (FindRepo(membership.Repo.Id) is not null)
        {
            return;
        }

        var joined = MapRepoModel(membership);

        Repos.Add(joined);
        CatchUpGame(joined);
        RepoCreated?.Invoke(membership.Repo.Id);
    }

    public async Task Update(Repo repo, string name, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        var request = new UpdateRepoRequest()
        {
            Name = name,
            AdapterConfiguration = baseSettings.Serialize()
        };

        var updated = await repoClient.UpdateRepoV1Async(repo.Id, request, cancellationToken);

        repo.Apply(updated);
    }

    /// <summary>
    /// Every repo here came out of one account's memberships, so a different user starts from an
    /// empty list rather than from one the next refresh would have to contradict.
    /// </summary>
    public void ClearUserState()
    {
        for (var i = Repos.Count - 1; i >= 0; i--)
        {
            Remove(Repos[i]);
        }

        // Back to "not asked yet", which is what this now is: the new account's repos are unknown
        // rather than known to be none, and a notice reading the difference must not answer for the
        // account that just left.
        HasLoaded = false;

        SetPendingChanges(null);
    }

    public async Task DeleteRepo(Guid id, CancellationToken cancellationToken)
    {
        await repoClient.DeleteRepoV1Async(id, cancellationToken);

        if (FindRepo(id) is Repo removed)
        {
            Remove(removed);
        }
    }

    public async Task ArchiveRepo(Guid id, CancellationToken cancellationToken)
    {
        await repoClient.ArchiveRepoV1Async(id, cancellationToken);

        if (FindRepo(id) is Repo archived)
        {
            Remove(archived);
        }
    }

    public async Task RestoreRepo(Guid id, CancellationToken cancellationToken)
    {
        await repoClient.RestoreRepoV1Async(id, cancellationToken);

        // Refetched rather than constructed here: a Repo wraps a membership, hydrates an adapter and
        // holds a collection synchronizer, and half-building one from a restore response is how the
        // two get to disagree.
        await RefreshRepos(cancellationToken);
    }

    public async Task<IReadOnlyList<RepoMembershipDto>> GetArchivedRepos(CancellationToken cancellationToken)
    {
        return [.. await repoClient.GetArchivedReposV1Async(cancellationToken)];
    }


    private Repo? FindRepo(Guid id)
    {
        return Repos.FirstOrDefault(x => x.Id == id);
    }

    private List<RepoListEntry> Snapshot()
    {
        return [.. Repos.Select(x => x.ToListEntry())];
    }

    private void SetPendingChanges(RemoteChanges? changes)
    {
        if (PendingChanges is null && changes is null)
        {
            return;
        }

        PendingChanges = changes;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Remove(Repo repo)
    {
        Repos.Remove(repo);
        repo.Dispose();
    }

    private Repo MapRepoModel(RepoMembershipDto repoMembership)
    {
        return new Repo(repoMembership, gameAdapterIndex, this, gameRepository);
    }
}
