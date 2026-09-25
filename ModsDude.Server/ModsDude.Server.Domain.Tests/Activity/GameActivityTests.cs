using ModsDude.Server.Domain.Activity;
using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Activity;

public class GameActivityTests
{
    private static readonly UserId _user = new("anton");
    private static readonly GameKey _game = new("_farming_simulator#fs25");
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly ProfileId _profile = new(Guid.NewGuid());
    private static readonly ProfileId _otherProfile = new(Guid.NewGuid());
    private static readonly DateTime _start = new(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);


    /// <summary>
    /// A re-apply is the commonest gesture in the app and tells nobody anything, so it must not read
    /// as a change - but it is still somebody playing, and the lists sort on that.
    /// </summary>
    [Fact]
    public void A_reapply_of_the_same_profile_touches_the_row_without_changing_it()
    {
        var activity = CreateActivity();

        activity.Record(_repoId, _profile, null, GameActivityKind.Reapplied, null, _start.AddHours(2));

        Assert.Equal(_start, activity.ChangedAt);
        Assert.Equal(_start.AddHours(2), activity.TouchedAt);
        Assert.Equal(GameActivityKind.Activated, activity.Kind);
    }

    /// <summary>
    /// The client only says re-apply where the game was already on the profile, so one naming another
    /// profile means the switch was never heard about - and whoever reads this has not heard of it either.
    /// </summary>
    [Fact]
    public void A_reapply_of_another_profile_is_recorded_as_a_switch()
    {
        var activity = CreateActivity();

        activity.Record(_repoId, _otherProfile, null, GameActivityKind.Reapplied, null, _start.AddHours(2));

        Assert.Equal(_otherProfile, activity.ProfileId);
        Assert.Equal(_start.AddHours(2), activity.ChangedAt);
        Assert.Equal(GameActivityKind.Activated, activity.Kind);
    }

    /// <summary>
    /// Checking out a savegame is news even when the game was already on its profile: it is what
    /// somebody joining them needs to know.
    /// </summary>
    [Fact]
    public void A_checkout_on_the_same_profile_is_a_change()
    {
        var activity = CreateActivity();
        var savegameId = new SavegameId(Guid.NewGuid());

        activity.Record(_repoId, _profile, new RevisionNumber(4), GameActivityKind.SavegameCheckedOut, savegameId, _start.AddHours(1));

        Assert.Equal(GameActivityKind.SavegameCheckedOut, activity.Kind);
        Assert.Equal(savegameId, activity.SavegameId);
        Assert.Equal(new RevisionNumber(4), activity.PinnedRevision);
        Assert.Equal(_start.AddHours(1), activity.ChangedAt);
    }

    /// <summary>
    /// A later activation moves the game off the savegame, so naming it would say they are still on it.
    /// </summary>
    [Fact]
    public void An_activation_after_a_checkout_forgets_the_savegame()
    {
        var activity = CreateActivity();

        activity.Record(_repoId, _profile, new RevisionNumber(4), GameActivityKind.SavegameCheckedOut, new SavegameId(Guid.NewGuid()), _start.AddHours(1));
        activity.Record(_repoId, _otherProfile, null, GameActivityKind.Activated, null, _start.AddHours(2));

        Assert.Null(activity.SavegameId);
        Assert.Null(activity.PinnedRevision);
    }

    /// <summary>
    /// A re-apply after the savegame is checked in puts the game back on head, which is the one thing
    /// about a re-apply a follower does need to know.
    /// </summary>
    [Fact]
    public void A_reapply_updates_the_pinned_revision()
    {
        var activity = CreateActivity();

        activity.Record(_repoId, _profile, new RevisionNumber(4), GameActivityKind.SavegameCheckedOut, new SavegameId(Guid.NewGuid()), _start.AddHours(1));
        activity.Record(_repoId, _profile, null, GameActivityKind.Reapplied, null, _start.AddHours(2));

        Assert.Null(activity.PinnedRevision);
    }

    [Fact]
    public void A_row_is_never_created_as_a_reapply()
    {
        var activity = new GameActivity(_user, _game, _repoId, _profile, null, GameActivityKind.Reapplied, null, _start);

        Assert.Equal(GameActivityKind.Activated, activity.Kind);
    }

    [Fact]
    public void A_game_must_be_named()
    {
        Assert.Throws<DomainValidationException>(() => new GameKey(" "));
    }


    private static GameActivity CreateActivity()
        => new(_user, _game, _repoId, _profile, null, GameActivityKind.Activated, null, _start);
}
