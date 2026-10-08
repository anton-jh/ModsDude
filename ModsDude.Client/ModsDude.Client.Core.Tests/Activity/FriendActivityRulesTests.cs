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
    /// Same profile, different mod list: a friend on an older revision is somewhere this game
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

        Assert.Equal("alex checked out 'Farm 3'", FriendActivityRules.Headline(Changed(row)));
        Assert.Equal("alex switched to 'Harvest'", FriendActivityRules.Headline(Changed(Row())));
    }

    /// <summary>
    /// One card per friend per game: a second switch replaces the first rather than stacking under it,
    /// and brings it back even where the first was dismissed.
    /// </summary>
    [Fact]
    public void A_later_change_keeps_the_key_and_changes_the_signature()
    {
        var environment = new Environment(FollowAvailability.Available);

        var first = Assert.Single(FriendActivityRules.BuildNotices([Changed(Row())], environment));
        var second = Assert.Single(FriendActivityRules.BuildNotices([Changed(Row(changedAt: _at.AddHours(1)))], environment));

        Assert.Equal(first.Key, second.Key);
        Assert.NotEqual(first.Signature, second.Signature);
        Assert.True(FriendActivityRules.Owns(first.Key));
    }

    [Fact]
    public void A_notice_offers_to_follow_only_where_it_would_change_something()
    {
        var available = Assert.Single(FriendActivityRules.BuildNotices([Changed(Row(pinned: 4))], new Environment(FollowAvailability.Available)));
        var alreadyOn = Assert.Single(FriendActivityRules.BuildNotices([Changed(Row(kind: GameActivityKind.SavegameCheckedOut))], new Environment(FollowAvailability.AlreadyOn)));

        var action = Assert.Single(available.Actions);
        Assert.Equal(NoticeActionKind.UseProfile, action.Kind);
        Assert.Equal(4, available.Subject?.Revision);

        Assert.Empty(alreadyOn.Actions);
        Assert.Equal("You are on this too.", alreadyOn.Footnote);
        Assert.Equal(NoticeSeverity.Info, alreadyOn.Severity);
    }

    /// <summary>
    /// A friend who switched onto what this game is already on has come to where this user is, and
    /// there is nothing to tell them.
    /// </summary>
    [Fact]
    public void A_switch_onto_what_this_game_is_already_on_is_not_announced()
    {
        var alreadyOn = new Environment(FollowAvailability.AlreadyOn);

        Assert.False(FriendActivityRules.IsWorthAnnouncing(Changed(Row()), alreadyOn));
        Assert.Empty(FriendActivityRules.BuildNotices([Changed(Row())], alreadyOn));
    }

    [Fact]
    public void A_switch_this_game_is_not_on_is_announced()
    {
        Assert.True(FriendActivityRules.IsWorthAnnouncing(Changed(Row()), new Environment(FollowAvailability.Available)));
        Assert.True(FriendActivityRules.IsWorthAnnouncing(Changed(Row()), new Environment(FollowAvailability.NotConnected)));
    }

    /// <summary>A check-out names the savegame they are playing, which being on the profile does not.</summary>
    [Fact]
    public void A_check_out_is_announced_even_onto_what_this_game_is_on()
    {
        Assert.True(FriendActivityRules.IsWorthAnnouncing(
            Changed(Row(kind: GameActivityKind.SavegameCheckedOut)), new Environment(FollowAvailability.AlreadyOn)));
    }

    [Fact]
    public void A_game_is_playing_until_its_last_heartbeat_runs_out()
    {
        var row = Playing(_at, until: _at.AddMinutes(3));

        Assert.True(FriendActivityRules.IsPlaying(row, _at.AddMinutes(2)));
        Assert.False(FriendActivityRules.IsPlaying(row, _at.AddMinutes(3)));
        Assert.False(FriendActivityRules.IsPlaying(Row(), _at));
    }

    [Fact]
    public void Starting_to_play_is_news_while_it_lasts()
    {
        var row = Playing(_at.AddHours(1), until: _at.AddHours(1).AddMinutes(3));

        var news = FriendActivityRules.ToNews(row, since: _at, now: _at.AddHours(1).AddMinutes(1));

        Assert.True(news?.IsPlaying);
        Assert.Null(FriendActivityRules.ToNews(row, since: _at, now: _at.AddHours(2)));
    }

    /// <summary>The switch is still what they are on, once the session it led to is over.</summary>
    [Fact]
    public void A_switch_this_session_is_the_news_again_once_they_stop_playing()
    {
        var row = Playing(_at.AddHours(1), until: _at.AddHours(1).AddMinutes(3), changedAt: _at.AddMinutes(30));

        var news = FriendActivityRules.ToNews(row, since: _at, now: _at.AddHours(2));

        Assert.False(news?.IsPlaying);
    }

    [Fact]
    public void A_session_from_before_the_news_began_is_not_news()
    {
        var row = Playing(_at, until: _at.AddHours(1));

        Assert.Null(FriendActivityRules.ToNews(row, since: _at, now: _at.AddMinutes(10)));
    }

    [Fact]
    public void News_is_as_new_as_the_latest_of_a_switch_and_a_start_of_play()
    {
        Assert.Equal(_at.AddHours(1), FriendActivityRules.NewsAt(Playing(_at.AddHours(1), until: _at.AddHours(2))));
        Assert.Equal(_at, FriendActivityRules.NewsAt(Row()));
    }

    [Fact]
    public void A_playing_card_says_so_and_replaces_the_switch_card()
    {
        var environment = new Environment(FollowAvailability.Available);
        var row = Playing(_at.AddHours(1), until: _at.AddHours(2));
        row.SavegameName = "Farm 3";
        row.Kind = GameActivityKind.SavegameCheckedOut;

        var switched = Assert.Single(FriendActivityRules.BuildNotices([Changed(Row())], environment));
        var playing = Assert.Single(FriendActivityRules.BuildNotices([new FriendNews(row, IsPlaying: true)], environment));

        Assert.Equal("alex is playing", playing.Headline);
        Assert.Equal("Farming Simulator 25, 'Farm 3', on 'Harvest' in Our Farm.", playing.Body);
        Assert.Equal(switched.Key, playing.Key);
        Assert.NotEqual(switched.Signature, playing.Signature);
    }

    /// <summary>Somebody already on the same profile is exactly who can join them at once.</summary>
    [Fact]
    public void Starting_to_play_is_announced_even_onto_what_this_game_is_on()
    {
        var row = Playing(_at, until: _at.AddHours(1));

        Assert.True(FriendActivityRules.IsWorthAnnouncing(new FriendNews(row, IsPlaying: true), new Environment(FollowAvailability.AlreadyOn)));
    }

    [Fact]
    public void Lists_put_whoever_is_playing_first()
    {
        var idle = Row(changedAt: _at.AddHours(2));
        var playing = Playing(_at, until: _at.AddHours(1));
        playing.User = new UserDto { Id = "bea", DisplayName = "bea", Tag = "0002" };

        Assert.Equal([playing, idle], FriendActivityRules.Order([idle, playing], _at.AddMinutes(1)));
        Assert.Equal([idle, playing], FriendActivityRules.Order([idle, playing], _at.AddHours(1)));
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

    private static GameActivityDto Playing(DateTime since, DateTime until, DateTime? changedAt = null)
    {
        var row = Row(changedAt: changedAt ?? _at);
        row.PlayingSince = since;
        row.PlayingUntil = until;
        row.TouchedAt = since;

        return row;
    }

    private static FriendNews Changed(GameActivityDto row) => new(row, IsPlaying: false);

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
