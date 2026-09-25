using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Tests.Activity;

public class FriendActivityServiceTests
{
    private static readonly DateTime _monday = new(2026, 9, 21, 18, 0, 0, DateTimeKind.Utc);


    /// <summary>
    /// A week of other people's evenings is history, not news: the first read an account ever makes
    /// is the baseline, and the column must not open on it.
    /// </summary>
    [Fact]
    public async Task The_first_read_ever_announces_nothing()
    {
        var harness = new Harness();
        harness.Client.Rows = [Row("alex", _monday)];

        await harness.Service.RefreshAsync(CancellationToken.None);

        Assert.Empty(harness.Announced);
        Assert.Empty(harness.Service.News);
        Assert.Single(harness.Service.Rows);
    }

    [Fact]
    public async Task A_change_after_the_first_read_is_announced_once()
    {
        var harness = new Harness();
        harness.Client.Rows = [Row("alex", _monday)];
        await harness.Service.RefreshAsync(CancellationToken.None);

        harness.Client.Rows = [Row("alex", _monday.AddHours(1))];
        await harness.Service.RefreshAsync(CancellationToken.None);
        await harness.Service.RefreshAsync(CancellationToken.None);

        var announced = Assert.Single(harness.Announced);
        Assert.Equal("alex", Assert.Single(announced).User.Id);
        Assert.Single(harness.Service.News);
    }

    /// <summary>
    /// A re-apply only moves the touched time, and the server says so by leaving the changed time
    /// alone - so it is not news, however recent.
    /// </summary>
    [Fact]
    public async Task A_row_touched_but_not_changed_is_not_news()
    {
        var harness = new Harness();
        harness.Client.Rows = [Row("alex", _monday)];
        await harness.Service.RefreshAsync(CancellationToken.None);

        harness.Client.Rows = [Row("alex", _monday, touchedAt: _monday.AddHours(3))];
        await harness.Service.RefreshAsync(CancellationToken.None);

        Assert.Empty(harness.Announced);
        Assert.Empty(harness.Service.News);
    }

    /// <summary>What happened while the app was closed is exactly what somebody opening it wants to hear.</summary>
    [Fact]
    public async Task What_changed_since_the_last_run_is_announced_on_the_first_read()
    {
        var harness = new Harness();
        harness.Seen.Set("me", _monday);
        harness.Client.Rows = [Row("alex", _monday.AddHours(2)), Row("bea", _monday.AddHours(-2))];

        await harness.Service.RefreshAsync(CancellationToken.None);

        var announced = Assert.Single(harness.Announced);
        Assert.Equal("alex", Assert.Single(announced).User.Id);
        Assert.Equal(_monday.AddHours(2), harness.Seen.Get("me"));
    }

    [Fact]
    public async Task Signing_out_forgets_the_rows()
    {
        var harness = new Harness();
        harness.Client.Rows = [Row("alex", _monday)];
        await harness.Service.RefreshAsync(CancellationToken.None);

        harness.Service.ClearUserState();

        Assert.Empty(harness.Service.Rows);
        Assert.False(harness.Service.HasLoaded);
    }


    private static GameActivityDto Row(string userId, DateTime changedAt, DateTime? touchedAt = null) => new()
    {
        User = new UserDto { Id = userId, DisplayName = userId, Tag = "0001" },
        Game = "_farming_simulator#fs25",
        RepoId = Guid.Empty,
        ProfileId = Guid.Empty,
        ProfileName = "Harvest",
        Kind = GameActivityKind.Activated,
        ChangedAt = changedAt,
        TouchedAt = touchedAt ?? changedAt
    };


    private sealed class Harness
    {
        public Harness()
        {
            Service = new FriendActivityService(Client, new FixedUser("me"), Seen);
            Service.Announced += (_, rows) => Announced.Add(rows);
        }

        public FakeActivityClient Client { get; } = new();
        public MemorySeen Seen { get; } = new();
        public FriendActivityService Service { get; }
        public List<IReadOnlyList<GameActivityDto>> Announced { get; } = [];
    }

    private sealed class FakeActivityClient : IActivityClient
    {
        public List<GameActivityDto> Rows { get; set; } = [];

        public Task<ICollection<GameActivityDto>> GetGameActivityV1Async(Guid? repoId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ICollection<GameActivityDto>>([.. Rows]);

        public Task RecordGameActivityV1Async(RecordGameActivityRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearGameActivityV1Async(string game, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FixedUser(string id) : CurrentUserService(null!)
    {
        public override Task<CurrentUserDto> Get(CancellationToken cancellationToken)
            => Task.FromResult(new CurrentUserDto { Id = id, DisplayName = id });
    }

    private sealed class MemorySeen : IFriendActivitySeen
    {
        private readonly Dictionary<string, DateTime> _seen = [];

        public DateTime? Get(string userId) => _seen.TryGetValue(userId, out var until) ? until : null;

        public void Set(string userId, DateTime until) => _seen[userId] = until;
    }
}
