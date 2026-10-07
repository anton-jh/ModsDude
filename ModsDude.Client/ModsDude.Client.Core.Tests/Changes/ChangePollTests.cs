using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.Changes;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Mods;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.Profiles;
using ModsDude.Client.Core.Tests.Savegames;
using ModsDude.Client.Core.Tests.Stores;
using ModsDude.Client.Core.Tests.Users;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Tests.Changes;

/// <summary>One small read of the server's change counters, and reading again only what moved.</summary>
public class ChangePollTests
{
    [Fact]
    public async Task The_first_poll_reads_the_repo_list_and_every_loaded_store()
    {
        var harness = new Harness();
        await harness.LoadProfilesAsync();
        await harness.LoadSavegamesAsync();

        await harness.PollAsync();

        Assert.Equal(1, harness.Repos.Refreshes);
        Assert.Equal(2, harness.ProfilesServer.Reads);
        Assert.Equal(2, harness.SavegameServer.ListReads);
        Assert.Equal(1, harness.Friends.Refreshes);
    }

    [Fact]
    public async Task A_poll_where_nothing_moved_reads_nothing()
    {
        var harness = new Harness();
        await harness.LoadProfilesAsync();
        await harness.LoadSavegamesAsync();
        await harness.PollAsync();
        var before = harness.Reads();

        await harness.PollAsync();

        Assert.Equal(before, harness.Reads());
    }

    [Fact]
    public async Task Only_the_store_whose_counter_moved_is_read_again()
    {
        var harness = new Harness();
        await harness.LoadProfilesAsync();
        await harness.LoadSavegamesAsync();
        await harness.PollAsync();
        var before = harness.Reads();

        harness.Changes.Move(harness.RepoId, x => x with { Profiles = x.Profiles + 1 });
        await harness.PollAsync();

        Assert.Equal(before with { Profiles = before.Profiles + 1 }, harness.Reads());
    }

    [Fact]
    public async Task The_profiles_of_a_repo_nothing_holds_and_no_game_follows_are_not_read()
    {
        var harness = new Harness();
        await harness.PollAsync();

        harness.Changes.Move(harness.RepoId, x => x with { Profiles = x.Profiles + 1 });
        await harness.PollAsync();

        Assert.Equal(0, harness.ProfilesServer.Reads);
    }

    /// <summary>The drift check compares a game against its profile's newest revision, so those profiles are kept current.</summary>
    [Fact]
    public async Task The_profiles_of_a_repo_a_game_here_follows_are_read_without_being_loaded()
    {
        var harness = new Harness();
        harness.Follow(harness.RepoId);
        await harness.PollAsync();

        harness.Changes.Move(harness.RepoId, x => x with { Profiles = x.Profiles + 1 });
        await harness.PollAsync();

        Assert.Equal(2, harness.ProfilesServer.Reads);
    }

    /// <summary>How somebody whose save was taken over hears about it, with nobody opening the Saves page.</summary>
    [Fact]
    public async Task A_held_save_taken_over_is_read_without_being_loaded_and_asks_for_a_drift_check()
    {
        var harness = new Harness();
        await harness.HoldAsync();
        await harness.PollAsync();
        var checks = harness.Drift.Checks;

        harness.SavegameServer.ListedCheckout = Checkout("bob", "Bob");
        harness.Changes.Move(harness.RepoId, x => x with { Savegames = x.Savegames + 1 });
        await harness.PollAsync();

        Assert.Equal(checks + 1, harness.Drift.Checks);
        Assert.False(harness.SavegameStore.GetClaim(harness.RepoId, harness.SavegameServer.SavegameId)?.IsYours);
    }

    [Fact]
    public async Task A_savegame_change_that_leaves_the_held_saves_alone_asks_for_no_drift_check()
    {
        var harness = new Harness();
        await harness.HoldAsync();
        await harness.PollAsync();
        var checks = harness.Drift.Checks;

        harness.Changes.Move(harness.RepoId, x => x with { Savegames = x.Savegames + 1 });
        await harness.PollAsync();

        Assert.Equal(checks, harness.Drift.Checks);
    }

    [Fact]
    public async Task Mods_are_read_again_only_for_a_repo_whose_mods_were_read_before()
    {
        var harness = new Harness();
        await harness.PollAsync();

        harness.Changes.Move(harness.RepoId, x => x with { Mods = x.Mods + 1 });
        await harness.PollAsync();

        Assert.Equal(0, harness.Mods.Reads);

        harness.Mods.Loaded.Add(harness.RepoId);
        harness.Changes.Move(harness.RepoId, x => x with { Mods = x.Mods + 1 });
        await harness.PollAsync();

        Assert.Equal(1, harness.Mods.Reads);
    }

    [Fact]
    public async Task A_repo_joined_elsewhere_reads_the_repo_list_and_friend_activity()
    {
        var harness = new Harness();
        await harness.PollAsync();
        var before = harness.Reads();

        harness.Changes.Add(Guid.NewGuid());
        await harness.PollAsync();

        Assert.Equal(before with { Repos = before.Repos + 1, Friends = before.Friends + 1 }, harness.Reads());
    }

    [Fact]
    public async Task A_repo_left_elsewhere_reads_the_repo_list_and_friend_activity()
    {
        var harness = new Harness();
        await harness.PollAsync();
        var before = harness.Reads();

        harness.Changes.Remove(harness.RepoId);
        await harness.PollAsync();

        Assert.Equal(before with { Repos = before.Repos + 1, Friends = before.Friends + 1 }, harness.Reads());
    }

    [Fact]
    public async Task A_read_that_failed_is_tried_again_on_the_next_poll()
    {
        var harness = new Harness();
        await harness.LoadProfilesAsync();
        await harness.PollAsync();

        harness.Changes.Move(harness.RepoId, x => x with { Profiles = x.Profiles + 1 });
        harness.Friends.FailNext = true;
        harness.Changes.Move(harness.RepoId, x => x with { Activity = x.Activity + 1 });
        await harness.PollAsync();
        var afterFailure = harness.Reads();

        await harness.PollAsync();

        Assert.Equal(afterFailure with { Profiles = afterFailure.Profiles + 1, Friends = afterFailure.Friends + 1 }, harness.Reads());

        await harness.PollAsync();

        Assert.Equal(afterFailure with { Profiles = afterFailure.Profiles + 1, Friends = afterFailure.Friends + 1 }, harness.Reads());
    }

    [Fact]
    public async Task After_a_user_change_the_next_poll_reads_everything_again()
    {
        var harness = new Harness();
        await harness.LoadProfilesAsync();
        await harness.PollAsync();
        var before = harness.Reads();

        harness.Poll.ClearUserState();
        await harness.PollAsync();

        Assert.Equal(before with { Repos = before.Repos + 1, Profiles = before.Profiles + 1, Friends = before.Friends + 1 }, harness.Reads());
    }

    [Fact]
    public async Task A_poll_still_out_when_the_user_changes_leaves_nothing_behind_for_the_next_user()
    {
        var harness = new Harness();
        var gate = harness.Changes.Hold();

        var poll = harness.PollAsync();
        harness.Poll.ClearUserState();
        gate.SetResult();
        await poll;
        var before = harness.Reads();

        await harness.PollAsync();

        Assert.Equal(before with { Repos = before.Repos + 1, Friends = before.Friends + 1 }, harness.Reads());
    }

    /// <summary>Offline the reconnect probe is what asks; a poll would only fail.</summary>
    [Fact]
    public async Task A_poll_while_offline_reads_nothing()
    {
        var harness = new Harness();
        await harness.LoadProfilesAsync();
        harness.Connection.ReportUnreachable();
        var before = harness.Reads();

        await harness.PollAsync();

        Assert.Equal(before, harness.Reads());
        Assert.Equal(0, harness.Changes.Reads);
    }

    [Fact]
    public async Task Reading_everything_again_reads_every_loaded_store_though_nothing_moved()
    {
        var harness = new Harness();
        await harness.LoadProfilesAsync();
        await harness.LoadSavegamesAsync();
        await harness.PollAsync();
        var before = harness.Reads();

        await harness.Poll.RereadAllAsync(CancellationToken.None);

        Assert.Equal(before with
        {
            Repos = before.Repos + 1,
            Profiles = before.Profiles + 1,
            Savegames = before.Savegames + 1,
            Friends = before.Friends + 1
        }, harness.Reads());

        await harness.PollAsync();

        Assert.Equal(before with
        {
            Repos = before.Repos + 1,
            Profiles = before.Profiles + 1,
            Savegames = before.Savegames + 1,
            Friends = before.Friends + 1
        }, harness.Reads());
    }

    [Fact]
    public async Task Cancelling_a_poll_stops_it()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Poll.PollAsync(cancellation.Token));
    }


    private static SavegameCheckoutDto Checkout(string userId, string name) => new()
    {
        Id = Guid.NewGuid(),
        User = new UserDto { Id = userId, DisplayName = name, Tag = "0001" },
        TakenAt = DateTime.UtcNow.AddHours(-1),
        Status = SavegameCheckoutStatus.Held
    };


    private sealed record ReadCounts(int Repos, int Profiles, int Savegames, int Mods, int Friends);

    private sealed class Harness
    {
        private readonly FakeGameState _state = new();
        private readonly SavegameBindingStore _bindings;
        private readonly Candidates _candidates = new();


        public Harness()
        {
            _state.Add(Keys.Game(), new PersistedGame
            {
                GameAdapterId = new GameAdapterId("farmingSimulator", 1),
                Name = "Farming Simulator 25",
                AdapterLocalSettings = "{}",
                Targets = []
            });

            _bindings = new SavegameBindingStore(_state);

            var currentUser = new FixedCurrentUser("me");

            ProfileStore = new ProfileStore(ProfilesServer, InlineStoreDispatcher.Instance, NullLogger<ProfileStore>.Instance);
            SavegameStore = new SavegameStore(SavegameServer, currentUser, InlineStoreDispatcher.Instance, NullLogger<SavegameStore>.Instance);
            Changes.Add(RepoId);

            Poll = new ChangePoll(
                Changes,
                Repos,
                ProfileStore,
                SavegameStore,
                Mods,
                Friends,
                new HeldSavegameClaims(_candidates, _bindings, SavegameStore),
                _candidates,
                Drift,
                currentUser,
                Connection,
                NullLogger<ChangePoll>.Instance);
        }


        public FakeChangesServer Changes { get; } = new();
        public ServerConnection Connection { get; } = new();
        public FakeRepoStore Repos { get; } = new();
        public FakeProfilesServer ProfilesServer { get; } = new();
        public FakeSavegameServer SavegameServer { get; } = new();
        public FakeModStore Mods { get; } = new();
        public FakeFriends Friends { get; } = new();
        public FakeDriftMonitor Drift { get; } = new();
        public ProfileStore ProfileStore { get; }
        public SavegameStore SavegameStore { get; }
        public ChangePoll Poll { get; }

        /// <summary>The savegame fake answers for one repo, so every test uses that one.</summary>
        public Guid RepoId => SavegameServer.RepoId;


        public Task PollAsync() => Poll.PollAsync(CancellationToken.None);

        public ReadCounts Reads() => new(Repos.Refreshes, ProfilesServer.Reads, SavegameServer.ListReads, Mods.Reads, Friends.Refreshes);

        public Task LoadProfilesAsync() => ProfileStore.RefreshAsync(RepoId, CancellationToken.None);

        public Task LoadSavegamesAsync() => SavegameStore.RefreshAsync(RepoId, CancellationToken.None);

        public void Follow(Guid repoId) => _candidates.Following = new ActiveProfile(repoId, Guid.NewGuid());

        /// <summary>A save checked out here, and the store knowing it as the caller's.</summary>
        public async Task HoldAsync()
        {
            SavegameServer.Seed([1, 2, 3]);
            SavegameServer.ListedCheckout = Checkout("me", "Me");

            _bindings.SetBinding(Keys.Game(), new SavegameCheckoutBinding(
                RepoId,
                SavegameServer.SavegameId,
                Keys.Slot("savegame1"),
                1,
                "aaaa",
                DateTime.UtcNow));

            await LoadSavegamesAsync();
        }
    }


    private sealed class FakeChangesServer : IChangesClient
    {
        private readonly List<RepoChangesDto> _repos = [];
        private TaskCompletionSource? _held;


        public void Add(Guid repoId) => _repos.Add(new RepoChangesDto { RepoId = repoId, Repo = 1, Members = 1 });

        public void Remove(Guid repoId) => _repos.RemoveAll(x => x.RepoId == repoId);

        public void Move(Guid repoId, Func<RepoChangesDto, RepoChangesDto> move)
        {
            var index = _repos.FindIndex(x => x.RepoId == repoId);
            _repos[index] = move(_repos[index]);
        }

        /// <summary>Holds the next read until the test releases it.</summary>
        public TaskCompletionSource Hold()
            => _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Reads { get; private set; }

        public async Task<GetChangesResponse> GetChangesV1Async(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Reads++;

            var answer = new GetChangesResponse { Repos = [.. _repos] };

            if (_held is { } held)
            {
                _held = null;
                await held.Task.WaitAsync(cancellationToken);
            }

            return answer;
        }
    }

    /// <summary>The repo list, which a poll only ever reads again.</summary>
    private sealed class FakeRepoStore : IRepoStore
    {
        public event Action<Guid>? RepoCreated { add { } remove { } }

        public ObservableCollection<Repo> Repos { get; } = [];
        public bool HasLoaded => true;
        public int Refreshes { get; private set; }

        public Task RefreshRepos(CancellationToken cancellationToken)
        {
            Refreshes++;

            return Task.CompletedTask;
        }

        public bool IsGone(Guid repoId) => false;
        public void ClearUserState() { }

        public Task CreateRepo(string name, string adapterId, DynamicForm baseSettings, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepoMembershipDto> Join(Func<CancellationToken, Task<RepoMembershipDto>> redeem, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task Update(Repo repo, string name, DynamicForm baseSettings, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteRepo(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ArchiveRepo(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RestoreRepo(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RepoMembershipDto>> GetArchivedRepos(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeModStore : IModStore
    {
        public HashSet<Guid> Loaded { get; } = [];
        public int Reads { get; private set; }

        public Task<IReadOnlyList<ModDto>> GetAsync(Guid repoId, CancellationToken cancellationToken)
        {
            Reads++;

            return Task.FromResult<IReadOnlyList<ModDto>>([]);
        }

        public bool IsLoaded(Guid repoId) => Loaded.Contains(repoId);
        public void ClearUserState() { }
    }

    private sealed class FakeFriends : IFriendActivityService
    {
        public event EventHandler? Changed { add { } remove { } }
        public event EventHandler<IReadOnlyList<GameActivityDto>>? Announced { add { } remove { } }

        public IReadOnlyList<GameActivityDto> Rows => [];
        public bool HasLoaded => true;
        public IReadOnlyList<GameActivityDto> News => [];
        public int Refreshes { get; private set; }
        public bool FailNext { get; set; }

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            Refreshes++;

            if (FailNext)
            {
                FailNext = false;

                throw new HttpRequestException("The connection dropped.");
            }

            return Task.CompletedTask;
        }

        public void ClearUserState() { }
    }

    private sealed class FakeDriftMonitor : IDriftMonitor
    {
        public event EventHandler? Changed { add { } remove { } }

        public int Checks { get; private set; }

        public IReadOnlyList<TargetDrift> Drifted => [];
        public bool HasDrift => false;
        public IReadOnlyList<CorruptedBlob> StoreCorruption => [];
        public bool HasStoreCorruption => false;
        public bool HasAnything => false;

        public Task<bool> CheckAsync(DriftCheckReason reason = DriftCheckReason.Explicit)
        {
            Checks++;

            return Task.FromResult(false);
        }

        public bool Check(DriftCheckReason reason = DriftCheckReason.Explicit) => throw new NotSupportedException();
        public void Watch() { }
        public void Dispose() { }
    }

    private sealed class Candidates : IDriftCandidateSource
    {
        public ActiveProfile? Following { get; set; }

        public IReadOnlyList<DriftCandidate> GetDriftCandidates() => [new DriftCandidate(Keys.Game(), "FS25", [], Following)];
    }
}
