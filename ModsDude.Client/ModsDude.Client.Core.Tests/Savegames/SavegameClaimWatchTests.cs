using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// How a takeover reaches this machine without anybody opening the repo's Saves page.
/// </summary>
public class SavegameClaimWatchTests
{
    [Fact]
    public async Task A_machine_holding_nothing_asks_nothing()
    {
        var harness = new Harness();

        Assert.False(await harness.Watch.RefreshAsync(CancellationToken.None));
        Assert.Equal(0, harness.Server.ListReads);
    }

    [Fact]
    public async Task A_held_save_somebody_else_has_taken_is_recorded_as_theirs()
    {
        var harness = new Harness();
        harness.Hold();
        harness.Server.ListedCheckout = Checkout("bob", "Bob");

        Assert.True(await harness.Watch.RefreshAsync(CancellationToken.None));

        var claim = harness.Sightings.GetClaim(harness.Server.RepoId, harness.Server.SavegameId);

        Assert.NotNull(claim);
        Assert.False(claim.IsYours);
        Assert.Equal("Bob", claim.Holder?.DisplayName);
    }

    /// <summary>
    /// The answer is what decides whether the drift check runs again, so reading the same thing twice
    /// must not count as news.
    /// </summary>
    [Fact]
    public async Task Reading_the_same_answer_again_is_not_a_change()
    {
        var harness = new Harness();
        harness.Hold();
        harness.Server.ListedCheckout = Checkout("bob", "Bob");

        await harness.Watch.RefreshAsync(CancellationToken.None);

        Assert.False(await harness.Watch.RefreshAsync(CancellationToken.None));
        Assert.Equal(2, harness.Server.ListReads);
    }

    [Fact]
    public async Task A_held_save_still_yours_is_recorded_as_yours()
    {
        var harness = new Harness();
        harness.Hold();
        harness.Server.ListedCheckout = Checkout(Harness.Me, "Me");

        await harness.Watch.RefreshAsync(CancellationToken.None);

        Assert.True(harness.Sightings.GetClaim(harness.Server.RepoId, harness.Server.SavegameId)?.IsYours);
    }


    private static SavegameCheckoutDto Checkout(string userId, string name) => new()
    {
        Id = Guid.NewGuid(),
        User = new UserDto { Id = userId, DisplayName = name, Tag = "0001" },
        TakenAt = DateTime.UtcNow.AddHours(-1),
        Status = SavegameCheckoutStatus.Held
    };


    private sealed class Harness
    {
        public const string Me = "me";

        private readonly FakeGameState _state = new();
        private readonly SavegameBindingStore _bindings;


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

            Watch = new SavegameClaimWatch(
                new Candidates(),
                _bindings,
                Server,
                new CurrentUserService(new Users()),
                Sightings,
                NullLogger<SavegameClaimWatch>.Instance);
        }


        public FakeSavegameServer Server { get; } = new();
        public SavegameSightingCache Sightings { get; } = new();
        public SavegameClaimWatch Watch { get; }


        public void Hold() => _bindings.SetBinding(Keys.Game(), new SavegameCheckoutBinding(
            Server.RepoId,
            Server.SavegameId,
            Keys.Slot("savegame1"),
            1,
            "aaaa",
            DateTime.UtcNow));
    }

    private sealed class Candidates : IDriftCandidateSource
    {
        public IReadOnlyList<DriftCandidate> GetDriftCandidates() => [new DriftCandidate(Keys.Game(), "FS25", [], null)];
    }

    private sealed class Users : IUsersClient
    {
        public Task<ICollection<UserDto>> GetUsersV1Async(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CurrentUserDto> GetCurrentUserV1Async(CancellationToken cancellationToken = default)
            => Task.FromResult(new CurrentUserDto { Id = Harness.Me, DisplayName = "Me", Tag = "0002" });
    }
}
