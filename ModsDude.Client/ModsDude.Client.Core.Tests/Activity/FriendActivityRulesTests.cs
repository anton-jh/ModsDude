using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Notices;
using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Tests.Activity;

public class FriendActivityRulesTests
{
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();
    private static readonly DateTime _at = new(2026, 9, 21, 18, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void A_game_not_connected_here_cannot_follow()
    {
        Assert.Equal(FollowAvailability.NotConnected, FriendActivityRules.CanFollow(null, null, Row()));
    }

    [Fact]
    public void A_game_on_another_profile_can_follow()
    {
        var game = Game(new ActiveProfile(_repoId, Guid.NewGuid()));

        Assert.Equal(FollowAvailability.Available, FriendActivityRules.CanFollow(game, null, Row()));
    }

    [Fact]
    public void A_game_on_the_same_profile_at_head_is_already_on_it()
    {
        var game = Game(new ActiveProfile(_repoId, _profileId));

        Assert.Equal(FollowAvailability.AlreadyOn, FriendActivityRules.CanFollow(game, null, Row()));
    }

    /// <summary>
    /// Same profile, different mod list: a friend on a past savegame's revision is somewhere this game
    /// is not, and that is exactly when following them is worth a button.
    /// </summary>
    [Fact]
    public void A_friend_held_on_a_past_revision_can_be_followed_from_head()
    {
        var game = Game(new ActiveProfile(_repoId, _profileId));

        Assert.Equal(FollowAvailability.Available, FriendActivityRules.CanFollow(game, null, Row(pinned: 4)));
        Assert.Equal(FollowAvailability.AlreadyOn, FriendActivityRules.CanFollow(game, 4, Row(pinned: 4)));
    }

    /// <summary>
    /// The revision is only ever named where the friend is held on one. An ordinary activation follows
    /// the profile, whatever number head happens to be.
    /// </summary>
    [Fact]
    public void The_follow_button_names_a_revision_only_where_the_friend_is_held_on_one()
    {
        Assert.Equal("Use this profile", FriendActivityRules.FollowLabel(Row()));
        Assert.Equal("Use rev 4", FriendActivityRules.FollowLabel(Row(pinned: 4)));
    }

    [Fact]
    public void A_check_out_is_headlined_with_the_savegame()
    {
        var row = Row(kind: GameActivityKind.SavegameCheckedOut);
        row.SavegameName = "Farm 3";

        Assert.Equal("alex checked out 'Farm 3'", FriendActivityRules.Headline(row));
        Assert.Equal("alex switched to 'Harvest'", FriendActivityRules.Headline(Row()));
    }

    /// <summary>
    /// One card per friend per game: a second switch replaces the first rather than stacking under it,
    /// and brings it back even where the first was dismissed.
    /// </summary>
    [Fact]
    public void A_later_change_keeps_the_key_and_changes_the_signature()
    {
        var environment = new Environment(FollowAvailability.Available);

        var first = Assert.Single(FriendActivityRules.BuildNotices([Row()], environment));
        var second = Assert.Single(FriendActivityRules.BuildNotices([Row(changedAt: _at.AddHours(1))], environment));

        Assert.Equal(first.Key, second.Key);
        Assert.NotEqual(first.Signature, second.Signature);
        Assert.True(FriendActivityRules.Owns(first.Key));
    }

    [Fact]
    public void A_notice_offers_to_follow_only_where_it_would_change_something()
    {
        var available = Assert.Single(FriendActivityRules.BuildNotices([Row(pinned: 4)], new Environment(FollowAvailability.Available)));
        var alreadyOn = Assert.Single(FriendActivityRules.BuildNotices([Row()], new Environment(FollowAvailability.AlreadyOn)));

        var action = Assert.Single(available.Actions);
        Assert.Equal(NoticeActionKind.UseProfile, action.Kind);
        Assert.Equal(4, available.Subject?.Revision);

        Assert.Empty(alreadyOn.Actions);
        Assert.Equal("You are on this too.", alreadyOn.Footnote);
        Assert.Equal(NoticeSeverity.Info, alreadyOn.Severity);
    }


    private static GameActivityDto Row(int? pinned = null, GameActivityKind kind = GameActivityKind.Activated, DateTime? changedAt = null) => new()
    {
        User = new UserDto { Id = "alex", DisplayName = "alex", Tag = "0001" },
        Game = "_farming_simulator#fs25",
        RepoId = _repoId,
        ProfileId = _profileId,
        ProfileName = "Harvest",
        PinnedRevision = pinned,
        Kind = kind,
        ChangedAt = changedAt ?? _at,
        TouchedAt = changedAt ?? _at
    };

    private static Game Game(ActiveProfile active) => new(GameIdentity.Parse("_farming_simulator#fs25"), new PersistedGame
    {
        GameAdapterId = new GameAdapterId("_farming_simulator", 1),
        Name = "Farming Simulator 25",
        AdapterLocalSettings = "{}",
        ActiveProfile = active
    });

    private sealed class Environment(FollowAvailability availability) : IFriendActivityEnvironment
    {
        public string DescribeGame(string game) => "Farming Simulator 25";

        public string? DescribeRepo(Guid repoId) => "Our Farm";

        public FollowAvailability CanFollow(GameActivityDto activity) => availability;
    }
}
