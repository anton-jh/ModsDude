using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Tests.Stores;
using ModsDude.Client.Core.Tests.Users;

namespace ModsDude.Client.Core.Tests.Savegames;

public class SavegameStoreTests
{
    private static readonly Guid _repoId = Guid.NewGuid();


    [Fact]
    public async Task A_read_holds_the_repos_savegames()
    {
        var harness = new Harness();
        var savegame = harness.Server.Add("Harvest");

        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);

        Assert.Equal("Harvest", Assert.Single(harness.Store.Live(_repoId)).Name);
        Assert.Equal("Harvest", harness.Store.Find(_repoId, savegame.Id)?.Name);
        Assert.Equal([_repoId], harness.Store.LoadedRepos);
    }

    [Fact]
    public async Task Ensuring_a_repo_is_loaded_reads_it_once()
    {
        var harness = new Harness();
        harness.Server.Add("Harvest");

        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);
        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);

        Assert.Equal(1, harness.Server.LiveReads);
    }

    /// <summary>
    /// A read that changed nothing says so, or every page drawn from the store would redraw on every
    /// read of an unchanged list.
    /// </summary>
    [Fact]
    public async Task A_read_that_finds_nothing_new_announces_nothing()
    {
        var harness = new Harness();
        harness.Server.Add("Harvest", head: Snapshot(2));
        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);
        var announced = harness.Announced();

        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);

        Assert.Empty(announced);
    }

    [Fact]
    public async Task A_read_that_finds_a_change_announces_it()
    {
        var harness = new Harness();
        var savegame = harness.Server.Add("Harvest");
        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);
        var announced = harness.Announced();

        harness.Server.Replace(savegame with { Name = "Winter" });
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);

        Assert.Equal([_repoId], announced);
        Assert.Equal("Winter", harness.Store.Find(_repoId, savegame.Id)?.Name);
    }

    [Fact]
    public async Task The_head_is_what_the_last_read_said()
    {
        var harness = new Harness();
        var savegame = harness.Server.Add("Harvest", head: Snapshot(4));

        Assert.Null(harness.Store.GetHeadSnapshot(_repoId, savegame.Id));

        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);

        Assert.Equal(4, harness.Store.GetHeadSnapshot(_repoId, savegame.Id));
    }

    [Fact]
    public async Task A_claim_is_yours_only_where_you_hold_it()
    {
        var harness = new Harness();
        var mine = harness.Server.Add("Mine", checkout: Checkout("me"));
        var theirs = harness.Server.Add("Theirs", checkout: Checkout("bob"));
        var ended = harness.Server.Add("Ended", checkout: Checkout("bob") with { Status = SavegameCheckoutStatus.Ended });
        var free = harness.Server.Add("Free");

        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);

        Assert.True(harness.Store.GetClaim(_repoId, mine.Id)?.IsYours);
        Assert.Equal(("bob", false), (harness.Store.GetClaim(_repoId, theirs.Id)?.Holder?.UserId, harness.Store.GetClaim(_repoId, theirs.Id)!.IsYours));
        Assert.Null(harness.Store.GetClaim(_repoId, ended.Id)?.Holder);
        Assert.Null(harness.Store.GetClaim(_repoId, free.Id)?.Holder);
    }

    /// <summary>
    /// Whose a claim is decides everything the drift check says about it, so without the signed-in user
    /// there is no answer rather than one reading every claim as somebody else's.
    /// </summary>
    [Fact]
    public async Task Without_the_signed_in_user_a_claim_has_no_answer()
    {
        var harness = new Harness(signedIn: false);
        var mine = harness.Server.Add("Mine", checkout: Checkout("me"));

        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);

        Assert.Null(harness.Store.GetClaim(_repoId, mine.Id));
    }

    [Fact]
    public async Task A_claim_just_taken_is_yours_before_the_next_read()
    {
        var harness = new Harness();
        var savegame = harness.Server.Add("Harvest", checkout: Checkout("bob"));
        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);

        harness.Store.RecordOwnClaim(_repoId, savegame.Id, Checkout("me"));

        Assert.True(harness.Store.GetClaim(_repoId, savegame.Id)?.IsYours);
    }

    [Fact]
    public async Task A_write_reads_the_repo_again_afterwards()
    {
        var harness = new Harness();
        var savegame = harness.Server.Add("Harvest");
        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);

        var answer = await harness.Store.WriteAsync(
            _repoId,
            _ =>
            {
                harness.Server.Replace(savegame with { Name = "Winter" });

                return Task.FromResult(42);
            },
            CancellationToken.None);

        Assert.Equal(42, answer);
        Assert.Equal("Winter", harness.Store.Find(_repoId, savegame.Id)?.Name);
    }

    /// <summary>
    /// The write has happened, so the read after it failing must not report it as failed: a caller told
    /// otherwise would skip the local half of what it just did.
    /// </summary>
    [Fact]
    public async Task A_write_that_landed_succeeds_even_where_the_read_after_it_fails()
    {
        var harness = new Harness();
        harness.Server.Add("Harvest");
        await harness.Store.EnsureLoadedAsync(_repoId, CancellationToken.None);
        var written = false;

        await harness.Store.WriteAsync(
            _repoId,
            _ =>
            {
                written = true;
                harness.Server.FailReads = true;

                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(written);
    }

    [Fact]
    public async Task Archived_savegames_are_read_only_when_asked_for_and_then_kept_up_with()
    {
        var harness = new Harness();
        harness.Server.Add("Harvest");
        var archived = harness.Server.AddArchived("Old");

        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);

        Assert.Equal(0, harness.Server.ArchivedReads);
        Assert.Empty(harness.Store.Archived(_repoId));

        await harness.Store.EnsureArchivedLoadedAsync(_repoId, CancellationToken.None);
        await harness.Store.RefreshAsync(_repoId, CancellationToken.None);

        Assert.Equal(archived.Id, Assert.Single(harness.Store.Archived(_repoId)).Id);
        Assert.Equal(2, harness.Server.ArchivedReads);
    }

    [Fact]
    public async Task A_read_still_out_when_the_user_changes_holds_nothing_of_theirs()
    {
        var harness = new Harness();
        harness.Server.Add("Harvest");
        var gate = harness.Server.Hold();

        var read = harness.Store.RefreshAsync(_repoId, CancellationToken.None);
        harness.Store.ClearUserState();
        gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Empty(harness.Store.Live(_repoId));
        Assert.Empty(harness.Store.LoadedRepos);
    }


    private static SavegameSnapshotDto Snapshot(int number) => new()
    {
        Number = number,
        RepoId = _repoId,
        ContentHash = $"hash-{number}",
        Created = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedBy = new UserDto { Id = "me", DisplayName = "Me", Tag = "0001" },
        Details = []
    };

    private static SavegameCheckoutDto Checkout(string userId) => new()
    {
        Id = Guid.NewGuid(),
        RepoId = _repoId,
        User = new UserDto { Id = userId, DisplayName = userId, Tag = "0001" },
        TakenAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        Status = SavegameCheckoutStatus.Held
    };


    private sealed class Harness
    {
        public Harness(bool signedIn = true)
        {
            Store = new SavegameStore(
                Server,
                signedIn ? new FixedCurrentUser("me") : new SignedOut(),
                InlineStoreDispatcher.Instance,
                NullLogger<SavegameStore>.Instance);
        }

        public FakeSavegameList Server { get; } = new();
        public SavegameStore Store { get; }

        public List<Guid> Announced()
        {
            var announced = new List<Guid>();
            Store.Changed += announced.Add;

            return announced;
        }
    }

    private sealed class SignedOut : Core.Users.ICurrentUserStore
    {
        public CurrentUserDto? User => null;

        public event EventHandler? Changed { add { } remove { } }

        public Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CurrentUserDto> WriteAsync(Func<CancellationToken, Task<CurrentUserDto>> send, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void ClearUserState()
        {
        }
    }

    /// <summary>A repo's live and archived savegame lists, answered as copies the way the server does.</summary>
    private sealed class FakeSavegameList : ISavegamesClient
    {
        private readonly List<SavegameDto> _live = [];
        private readonly List<SavegameDto> _archived = [];
        private TaskCompletionSource? _held;

        public int LiveReads { get; private set; }
        public int ArchivedReads { get; private set; }
        public bool FailReads { get; set; }

        public TaskCompletionSource Hold()
            => _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public SavegameDto Add(string name, SavegameSnapshotDto? head = null, SavegameCheckoutDto? checkout = null)
        {
            var savegame = new SavegameDto { Id = Guid.NewGuid(), RepoId = _repoId, Name = name, Head = head, Checkout = checkout };
            _live.Add(savegame);

            return savegame;
        }

        public SavegameDto AddArchived(string name)
        {
            var savegame = new SavegameDto { Id = Guid.NewGuid(), RepoId = _repoId, Name = name, ArchivedAt = DateTime.UtcNow };
            _archived.Add(savegame);

            return savegame;
        }

        public void Replace(SavegameDto savegame)
            => _live[_live.FindIndex(x => x.Id == savegame.Id)] = savegame;

        public async Task<ICollection<SavegameDto>> GetSavegamesV1Async(Guid repoId, CancellationToken cancellationToken = default)
        {
            LiveReads++;
            List<SavegameDto> answer = [.. _live.Select(x => x with { })];

            if (_held is { } held)
            {
                _held = null;
                await held.Task.WaitAsync(cancellationToken);
            }

            return FailReads ? throw new HttpRequestException("down") : answer;
        }

        public Task<ICollection<SavegameDto>> GetArchivedSavegamesV1Async(Guid repoId, CancellationToken cancellationToken = default)
        {
            ArchivedReads++;

            return FailReads
                ? throw new HttpRequestException("down")
                : Task.FromResult<ICollection<SavegameDto>>([.. _archived.Select(x => x with { })]);
        }

        public Task<PublishSavegameResponse> PublishSavegameV1Async(Guid repoId, PublishSavegameRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteSavegameV1Async(Guid repoId, Guid savegameId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SavegameDto> UpdateSavegameV1Async(Guid repoId, Guid savegameId, UpdateSavegameRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ArchiveSavegameV1Async(Guid repoId, Guid savegameId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GetSavegameCheckoutsResponse> GetSavegameCheckoutsV1Async(Guid repoId, Guid savegameId, int? skip = null, int? limit = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckOutSavegameResponse> CheckOutSavegameV1Async(Guid repoId, Guid savegameId, CheckOutSavegameRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DiscardSavegameCheckoutV1Async(Guid repoId, Guid savegameId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestoreSavegameV1Async(Guid repoId, Guid savegameId, RestoreRequest? request = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GetSavegameSnapshotsResponse> GetSavegameSnapshotsV1Async(Guid repoId, Guid savegameId, int? skip = null, int? limit = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckInSavegameResponse> CheckInSavegameV1Async(Guid repoId, Guid savegameId, CheckInSavegameRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteSavegameSnapshotV1Async(Guid repoId, Guid savegameId, int number, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SavegameSnapshotDto> RestoreSavegameSnapshotV1Async(Guid repoId, Guid savegameId, int number, RestoreSavegameSnapshotRequest? request = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
