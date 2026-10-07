using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Stores;
using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Repos;
public class RepoStore(
    IReposClient repoClient,
    IGameAdapterIndex gameAdapterIndex,
    IGameRepository gameRepository,
    IStoreDispatcher dispatcher,
    ILogger<RepoStore> logger)
    : IRepoStore
{
    private readonly StoreLoads<WholeList> _loads = new(dispatcher, logger);

    public event Action<Guid>? RepoCreated;

    public ObservableCollection<Repo> Repos { get; } = [];

    public bool HasLoaded { get; private set; }

    /// <summary>
    /// The ids in <see cref="Repos"/>, or null before the list has been read. A snapshot replaced
    /// whole, because <see cref="IsGone"/> is asked off the UI thread while the collection is not
    /// safe to read there.
    /// </summary>
    private volatile FrozenSet<Guid>? _knownIds;


    public bool IsGone(Guid repoId)
        => _knownIds is FrozenSet<Guid> known && known.Contains(repoId) is false;

    public Task RefreshRepos(CancellationToken cancellationToken)
        => _loads.ReadAsync(default, repoClient.GetMyReposV1Async, ApplyList, cancellationToken);

    private void ApplyList(ICollection<RepoMembershipDto> reposFromApi)
    {
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
                Add(MapRepoModel(dto));
            }
        }

        foreach (var repo in Repos)
        {
            CatchUpGame(repo);
        }

        // Last, so that a listener woken by the collection changing above sees the list before it
        // sees the flag saying the list is complete.
        HasLoaded = true;
        PublishKnownIds();
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
        await _loads.WriteAsync(
            default,
            ct => repoClient.CreateRepoV1Async(request, ct),
            repo =>
            {
                // A read that crossed this write may have brought it in already.
                if (FindRepo(repo.Id) is null)
                {
                    // The creator is the repo's first Admin, so the response carries everything the list needs.
                    var created = MapRepoModel(new RepoMembershipDto()
                    {
                        Repo = repo,
                        MembershipLevel = RepoMembershipLevel.Admin
                    });

                    Add(created);

                    // Before the shell navigates to it, so it opens with its game already there.
                    CatchUpGame(created);
                }

                RepoCreated?.Invoke(repo.Id);
            },
            cancellationToken);
    }

    public Task<RepoMembershipDto> Join(Func<CancellationToken, Task<RepoMembershipDto>> redeem, CancellationToken cancellationToken)
        => _loads.WriteAsync(
            default,
            redeem,
            membership =>
            {
                if (FindRepo(membership.Repo.Id) is not null)
                {
                    return;
                }

                var joined = MapRepoModel(membership);

                Add(joined);
                CatchUpGame(joined);
                RepoCreated?.Invoke(membership.Repo.Id);
            },
            cancellationToken);

    public async Task Update(Repo repo, string name, DynamicForm baseSettings, CancellationToken cancellationToken)
    {
        var request = new UpdateRepoRequest()
        {
            Name = name,
            AdapterConfiguration = baseSettings.Serialize(),
            ExpectedVersion = repo.Version
        };

        try
        {
            await _loads.WriteAsync(default, ct => repoClient.UpdateRepoV1Async(repo.Id, request, ct), repo.Apply, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.RepoChanged)
        {
            // So that looking again shows what somebody else changed. Not reading it is no reason to
            // hide why the change was refused.
            try
            {
                await RefreshRepos(cancellationToken);
            }
            catch (Exception refresh) when (refresh is not OperationCanceledException)
            {
                logger.LogWarning(refresh, "Could not read the repos again after a change to repo {Repo} was refused.", repo.Id);
            }

            throw new UserFriendlyException("Somebody else changed this repo", "Look at it again and make your change again.", exception);
        }
    }

    /// <summary>
    /// Every repo here came out of one account's memberships, so a different user starts from an
    /// empty list rather than from one the next refresh would have to contradict.
    /// </summary>
    public void ClearUserState()
    {
        _loads.Reset();

        for (var i = Repos.Count - 1; i >= 0; i--)
        {
            Remove(Repos[i]);
        }

        // Back to "not asked yet", which is what this now is: the new account's repos are unknown
        // rather than known to be none, and a notice reading the difference must not answer for the
        // account that just left.
        HasLoaded = false;
        PublishKnownIds();
    }

    public Task DeleteRepo(Guid id, CancellationToken cancellationToken)
        => _loads.WriteAsync(default, ct => repoClient.DeleteRepoV1Async(id, ct), () => RemoveIfHeld(id), cancellationToken);

    public Task ArchiveRepo(Guid id, CancellationToken cancellationToken)
        => _loads.WriteAsync(default, ct => repoClient.ArchiveRepoV1Async(id, ct), () => RemoveIfHeld(id), cancellationToken);

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

    private void RemoveIfHeld(Guid id)
    {
        if (FindRepo(id) is Repo held)
        {
            Remove(held);
        }
    }

    private void Add(Repo repo)
    {
        Repos.Add(repo);
        PublishKnownIds();
    }

    private void Remove(Repo repo)
    {
        Repos.Remove(repo);
        repo.Dispose();
        PublishKnownIds();
    }

    private void PublishKnownIds()
    {
        _knownIds = HasLoaded ? Repos.Select(x => x.Id).ToFrozenSet() : null;
    }

    private Repo MapRepoModel(RepoMembershipDto repoMembership)
    {
        return new Repo(repoMembership, gameAdapterIndex, this, gameRepository);
    }
}
