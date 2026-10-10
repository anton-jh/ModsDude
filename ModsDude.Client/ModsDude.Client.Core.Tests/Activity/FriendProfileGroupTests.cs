using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Tests.Activity;

public class FriendProfileGroupTests
{
    private static readonly Guid _repoId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid _otherRepoId = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid _harvest = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid _seeding = new("00000000-0000-0000-0000-00000000000b");
    private static readonly DateTime _at = new(2026, 9, 21, 18, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void Nothing_makes_no_groups()
    {
        Assert.Empty(FriendProfileGroup.Build([]));
    }

    [Fact]
    public void Friends_on_the_same_profile_share_a_group_most_recent_first()
    {
        var alex = Row("alex", _harvest, touchedAt: _at);
        var bea = Row("bea", _harvest, touchedAt: _at.AddHours(1));
        var cat = Row("cat", _harvest, touchedAt: _at.AddMinutes(30));

        var group = Assert.Single(FriendProfileGroup.Build([alex, bea, cat]));

        Assert.Equal([bea, cat, alex], group.Activities);
        Assert.Same(bea, group.Latest);
        Assert.Equal(_at.AddHours(1), group.LatestAt);
    }

    [Fact]
    public void Groups_are_ordered_by_their_most_recent_activity()
    {
        var oldHarvest = Row("alex", _harvest, touchedAt: _at);
        var seeding = Row("bea", _seeding, touchedAt: _at.AddHours(1));
        var newHarvest = Row("cat", _harvest, touchedAt: _at.AddHours(2));

        var groups = FriendProfileGroup.Build([oldHarvest, seeding, newHarvest]);

        Assert.Equal([_harvest, _seeding], groups.Select(x => x.ProfileId));
    }

    /// <summary>A friend held on a past revision is on a different mod list, so following them is a different follow.</summary>
    [Fact]
    public void A_held_revision_is_a_group_of_its_own()
    {
        var head = Row("alex", _harvest, touchedAt: _at);
        var held = Row("bea", _harvest, pinned: 4, touchedAt: _at.AddHours(1));
        var heldOlder = Row("cat", _harvest, pinned: 3, touchedAt: _at.AddHours(2));

        var groups = FriendProfileGroup.Build([head, held, heldOlder]);

        Assert.Equal([3, 4, (int?)null], groups.Select(x => x.PinnedRevision));
    }

    [Fact]
    public void The_same_profile_id_in_another_repo_is_another_group()
    {
        var groups = FriendProfileGroup.Build([Row("alex", _harvest, repoId: _repoId), Row("bea", _harvest, repoId: _otherRepoId)]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void Ties_are_broken_the_same_way_whatever_the_input_order()
    {
        var alex = Row("alex", _harvest, touchedAt: _at);
        var bea = Row("bea", _harvest, touchedAt: _at);
        var seeding = Row("cat", _seeding, touchedAt: _at);

        var forwards = FriendProfileGroup.Build([alex, bea, seeding]);
        var backwards = FriendProfileGroup.Build([seeding, bea, alex]);

        Assert.Equal(forwards.Select(x => x.ProfileId), backwards.Select(x => x.ProfileId));
        Assert.Equal([_harvest, _seeding], forwards.Select(x => x.ProfileId));
        Assert.Equal([alex, bea], forwards[0].Activities);
        Assert.Equal([alex, bea], backwards[0].Activities);
    }

    [Fact]
    public void A_friend_on_the_profile_through_two_installations_is_shown_once_at_their_latest()
    {
        var older = Row("alex", _harvest, touchedAt: _at, game: "_farming_simulator#fs25");
        var newer = Row("alex", _harvest, touchedAt: _at.AddHours(1), game: "_farming_simulator#fs25-steam");

        var group = Assert.Single(FriendProfileGroup.Build([older, newer]));

        Assert.Same(newer, Assert.Single(group.Activities));
    }

    [Fact]
    public void The_profile_is_named_as_its_most_recent_activity_names_it()
    {
        var before = Row("alex", _harvest, touchedAt: _at, profileName: "Harvest");
        var after = Row("bea", _harvest, touchedAt: _at.AddHours(1), profileName: "Harvest 2");

        Assert.Equal("Harvest 2", Assert.Single(FriendProfileGroup.Build([before, after])).ProfileName);
    }

    [Fact]
    public void A_group_is_playing_while_anyone_in_it_is()
    {
        var idle = Row("alex", _harvest, touchedAt: _at.AddHours(1));
        var playing = Row("bea", _harvest, touchedAt: _at);
        playing.PlayingSince = _at;
        playing.PlayingUntil = _at.AddMinutes(10);

        var group = Assert.Single(FriendProfileGroup.Build([idle, playing]));

        Assert.True(group.IsPlaying(_at.AddMinutes(5)));
        Assert.False(group.IsPlaying(_at.AddMinutes(10)));
    }


    private static GameActivityDto Row(
        string user,
        Guid profileId,
        int? pinned = null,
        DateTime? touchedAt = null,
        Guid? repoId = null,
        string game = "_farming_simulator#fs25",
        string profileName = "Harvest") => new()
    {
        User = new UserDto { Id = user, DisplayName = user, Tag = "0001" },
        Game = game,
        RepoId = repoId ?? _repoId,
        ProfileId = profileId,
        ProfileName = profileName,
        PinnedRevision = pinned,
        Kind = GameActivityKind.Activated,
        ChangedAt = touchedAt ?? _at,
        TouchedAt = touchedAt ?? _at
    };
}
