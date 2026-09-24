using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

public class SavegameSightingCacheTests
{
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _savegameId = Guid.NewGuid();


    [Fact]
    public void Nothing_read_is_nothing_known()
    {
        var cache = new SavegameSightingCache();

        Assert.Null(cache.GetHeadSnapshot(_repoId, _savegameId));
        Assert.Null(cache.GetClaim(_repoId, _savegameId));
    }

    [Fact]
    public void A_list_naming_somebody_else_records_their_claim()
    {
        var cache = new SavegameSightingCache();

        cache.Record(_repoId, [Savegame(Checkout("bob", SavegameCheckoutStatus.Held))], currentUserId: "me");

        var claim = cache.GetClaim(_repoId, _savegameId);

        Assert.NotNull(claim);
        Assert.False(claim.IsYours);
        Assert.Equal("bob", claim.Holder?.UserId);
        Assert.Equal(7, cache.GetHeadSnapshot(_repoId, _savegameId));
    }

    [Fact]
    public void A_list_naming_nobody_records_that_nobody_holds_it()
    {
        var cache = new SavegameSightingCache();

        cache.Record(_repoId, [Savegame(null)], currentUserId: "me");

        Assert.Equal(new SavegameClaimSighting(null, IsYours: false), cache.GetClaim(_repoId, _savegameId));
    }

    /// <summary>
    /// Not knowing who is signed in cannot tell a claim of your own from anybody else's, and reading
    /// every claim as somebody else's would report every held save as taken over.
    /// </summary>
    [Fact]
    public void A_list_read_without_knowing_who_is_signed_in_leaves_the_claims_alone()
    {
        var cache = new SavegameSightingCache();

        cache.RecordOwnClaim(_repoId, _savegameId, Checkout("me", SavegameCheckoutStatus.Held));
        cache.Record(_repoId, [Savegame(Checkout("bob", SavegameCheckoutStatus.Held))], currentUserId: null);

        Assert.True(cache.GetClaim(_repoId, _savegameId)?.IsYours);
        Assert.Equal(7, cache.GetHeadSnapshot(_repoId, _savegameId));
    }

    [Fact]
    public void A_claim_just_taken_replaces_the_sighting_from_before_it()
    {
        var cache = new SavegameSightingCache();

        cache.Record(_repoId, [Savegame(Checkout("bob", SavegameCheckoutStatus.Held))], currentUserId: "me");
        cache.RecordOwnClaim(_repoId, _savegameId, Checkout("me", SavegameCheckoutStatus.Held));

        Assert.True(cache.GetClaim(_repoId, _savegameId)?.IsYours);
    }


    private static SavegameDto Savegame(SavegameCheckoutDto? checkout) => new()
    {
        Id = _savegameId,
        RepoId = _repoId,
        Name = "Season 4",
        Created = DateTime.UtcNow,
        Head = new SavegameSnapshotDto { RepoId = _repoId, SavegameId = _savegameId, Number = 7 },
        Checkout = checkout
    };

    private static SavegameCheckoutDto Checkout(string userId, SavegameCheckoutStatus status) => new()
    {
        Id = Guid.NewGuid(),
        RepoId = _repoId,
        SavegameId = _savegameId,
        User = new UserDto { Id = userId, DisplayName = userId, Tag = "0001" },
        TakenAt = DateTime.UtcNow.AddHours(-1),
        Status = status
    };
}
