using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Users;

namespace ModsDude.Client.Core.Changes;

/// <summary>
/// Keeps the current user and every loaded store in step with the server from one small read of
/// change counters.
/// </summary>
/// <remarks>
/// <para>
/// <b>A counter that moved is read again; nothing else is.</b> The counters a poll compares against
/// are those of the last poll that read everything it had to. A poll where any read failed keeps the
/// older ones, so the next poll reads the same parts again rather than forgetting them.
/// </para>
/// <para>
/// <b>A repo seen for the first time counts as moved everywhere</b>, so a store loaded just before the
/// first poll cannot keep what changed between its read and the counters.
/// </para>
/// <para>
/// <b>Profiles of a repo a game here follows are read even when nothing else holds them</b>, because
/// the drift check compares those games against their profile's newest revision. The same goes for
/// the savegames of a repo this machine holds a save in, and a change to such a save's head or claim
/// asks for a drift check: that is how somebody whose save was taken over hears about it.
/// </para>
/// </remarks>
public sealed class ChangePoll(
    IChangesClient changesClient,
    IRepoStore repoStore,
    IProfileStore profileStore,
    ISavegameStore savegameStore,
    IModStore modStore,
    IFriendActivityService friends,
    IHeldSavegameClaims heldClaims,
    IDriftCandidateSource games,
    IDriftMonitor driftMonitor,
    ICurrentUserStore currentUser,
    IServerConnection connection,
    ILogger<ChangePoll> logger)
    : IChangePoll
{
    private readonly Lock _lock = new();
    private Counters? _seen;
    private int _generation;


    public async Task PollAsync(CancellationToken cancellationToken)
    {
        // The reconnect probe is what notices the server coming back.
        if (connection.IsOnline is false)
        {
            return;
        }

        int generation;
        Counters? seen;

        lock (_lock)
        {
            generation = _generation;
            seen = _seen;
        }

        var response = await changesClient.GetChangesV1Async(cancellationToken);
        var answer = new Counters(response.User, response.Repos.ToDictionary(x => x.RepoId));

        var poll = new Poll(seen, answer, logger, cancellationToken);

        await RefreshUserAsync(poll);
        await RefreshReposAsync(poll);
        await RefreshProfilesAsync(poll);
        await RefreshModsAsync(poll);
        await RefreshSavegamesAsync(poll);
        await RefreshActivityAsync(poll);

        if (poll.Failed)
        {
            return;
        }

        lock (_lock)
        {
            // A poll that started before a user change or a reread says nothing about what came after.
            if (_generation == generation)
            {
                _seen = answer;
            }
        }
    }

    public Task RereadAllAsync(CancellationToken cancellationToken)
    {
        Forget();

        return PollAsync(cancellationToken);
    }

    public void ClearUserState() => Forget();


    /// <summary>Drops the counters compared against, and lets a poll already out know its answer is for an older state.</summary>
    private void Forget()
    {
        lock (_lock)
        {
            _generation++;
            _seen = null;
        }
    }


    private async Task RefreshUserAsync(Poll poll)
    {
        if (poll.UserMoved)
        {
            await poll.AttemptAsync(currentUser.RefreshAsync, "the current user");
        }
    }

    private async Task RefreshReposAsync(Poll poll)
    {
        if (poll.AnyRepoGone || poll.Answer.Any(x => poll.Moved(x, c => c.Repo) || poll.Moved(x, c => c.Members)))
        {
            await poll.AttemptAsync(repoStore.RefreshRepos, "the repo list");
        }
    }

    private async Task RefreshProfilesAsync(Poll poll)
    {
        var followed = games.GetDriftCandidates()
            .Select(x => x.ActiveProfile?.RepoId)
            .OfType<Guid>()
            .ToHashSet();

        foreach (var repo in poll.Answer.Where(x => poll.Moved(x, c => c.Profiles)))
        {
            if (profileStore.IsLoaded(repo.RepoId) || followed.Contains(repo.RepoId))
            {
                await poll.AttemptAsync(ct => profileStore.RefreshAsync(repo.RepoId, ct), $"the profiles of repo {repo.RepoId}");
            }
        }
    }

    private async Task RefreshModsAsync(Poll poll)
    {
        foreach (var repo in poll.Answer.Where(x => poll.Moved(x, c => c.Mods) && modStore.IsLoaded(x.RepoId)))
        {
            await poll.AttemptAsync(ct => modStore.GetAsync(repo.RepoId, ct), $"the mods of repo {repo.RepoId}");
        }
    }

    private async Task RefreshSavegamesAsync(Poll poll)
    {
        var held = heldClaims.Repos();
        var loaded = savegameStore.LoadedRepos;

        var moved = poll.Answer
            .Where(x => poll.Moved(x, c => c.Savegames) && (loaded.Contains(x.RepoId) || held.Contains(x.RepoId)))
            .ToList();

        if (moved.Count == 0)
        {
            return;
        }

        var before = heldClaims.Capture();

        // Whose a claim is cannot be told until the store knows who is asking.
        await poll.AttemptAsync(currentUser.GetAsync, "the current user");

        foreach (var repo in moved)
        {
            await poll.AttemptAsync(ct => savegameStore.RefreshAsync(repo.RepoId, ct), $"the savegames of repo {repo.RepoId}");
        }

        if (heldClaims.Capture().SequenceEqual(before) is false)
        {
            await poll.AttemptAsync(_ => driftMonitor.CheckAsync(DriftCheckReason.Background), "the drift check after a held savegame changed");
        }
    }

    private async Task RefreshActivityAsync(Poll poll)
    {
        if (poll.AnyRepoGone || poll.Answer.Any(x => poll.Moved(x, c => c.Activity)))
        {
            await poll.AttemptAsync(friends.RefreshAsync, "which profiles friends are on");
        }
    }


    private sealed record Counters(long User, IReadOnlyDictionary<Guid, RepoChangesDto> Repos);

    private sealed class Poll(
        Counters? seen,
        Counters answer,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        /// <summary>The repos' counters, ordered by repo id.</summary>
        public IReadOnlyList<RepoChangesDto> Answer { get; } = [.. answer.Repos.Values.OrderBy(x => x.RepoId)];

        public bool Failed { get; private set; }

        /// <remarks>The first poll counts as moved.</remarks>
        public bool UserMoved => seen is null || seen.User != answer.User;

        /// <summary>A repo the last poll listed and this one does not: left, kicked from, archived or deleted.</summary>
        public bool AnyRepoGone => seen is not null && seen.Repos.Keys.Any(x => answer.Repos.ContainsKey(x) is false);

        /// <remarks>A repo with no counters from the last poll - joined since, or the first poll - counts as moved.</remarks>
        public bool Moved(RepoChangesDto now, Func<RepoChangesDto, long> counter)
            => seen is null || seen.Repos.TryGetValue(now.RepoId, out var before) is false || counter(before) != counter(now);

        /// <summary>One read, logged and remembered when it fails, so the rest of the poll still runs.</summary>
        public async Task AttemptAsync(Func<CancellationToken, Task> read, string what)
        {
            try
            {
                await read(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || cancellationToken.IsCancellationRequested is false)
            {
                Failed = true;
                logger.LogInformation(exception, "Could not read {What} again after it changed on the server.", what);
            }
        }
    }
}
