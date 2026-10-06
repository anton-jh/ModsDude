using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// The apply table, the revision a hold pins the folder to, and the one-mod-list-per-game limit.
/// </summary>
public class SavegameHoldRulesTests
{
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();
    private static readonly Guid _otherProfileId = Guid.NewGuid();
    private static readonly Guid _savegameId = Guid.NewGuid();


    [Fact]
    public void A_game_holding_nothing_constrains_nothing()
    {
        Assert.Null(SavegameHoldRules.RequiredRevision([], _profileId));
        Assert.True(SavegameHoldRules.DecideApply([], _profileId, 1004).IsAllowed);
        Assert.Null(SavegameHoldRules.FindConflictingHold([], _savegameId));
    }

    /// <summary>
    /// A savegame on latest follows its profile, so preparing the mod list
    /// before a session and then checking the savegame out has to leave the new list in place.
    /// </summary>
    [Fact]
    public void A_savegame_on_latest_pins_nothing_and_refuses_nothing()
    {
        var held = new[] { Hold() };

        Assert.Null(SavegameHoldRules.RequiredRevision(held, _profileId));
        Assert.True(SavegameHoldRules.DecideApply(held, _profileId, 1004).IsAllowed);
    }

    [Fact]
    public void A_savegame_in_compatibility_mode_pins_the_folder_to_its_revision()
    {
        var held = new[] { Hold(target: 4) };

        Assert.Equal(4, SavegameHoldRules.RequiredRevision(held, _profileId));

        // Re-applying it is the whole point: that is how folder drift under a pinned savegame gets repaired.
        Assert.True(SavegameHoldRules.DecideApply(held, _profileId, 4).IsAllowed);
    }

    [Fact]
    public void A_savegame_in_compatibility_mode_refuses_every_other_revision_including_head()
    {
        var decision = SavegameHoldRules.DecideApply([Hold(target: 4)], _profileId, 1004);

        Assert.Equal(SavegameApplyRefusal.CompatibilityModeIsHeld, decision.Refusal);
        Assert.Equal(_savegameId, decision.SavegameId);
        Assert.Equal(4, decision.Revision);
    }

    /// <summary>
    /// Null is not "head" here - it is "the caller has not chosen and will take whatever
    /// <c>RequiredRevision</c> says", which is exactly the pinned one. It cannot be the wrong revision
    /// when it is not a revision.
    /// </summary>
    [Fact]
    public void A_caller_that_names_no_revision_is_never_refused_for_the_wrong_one()
    {
        Assert.True(SavegameHoldRules.DecideApply([Hold(target: 4)], _profileId, revision: null).IsAllowed);
    }

    /// <summary>
    /// The active-profile switch, refused. Checked before any revision is looked at, because two
    /// profiles' revision numbers are not comparable at all.
    /// </summary>
    [Fact]
    public void A_savegame_following_another_profile_refuses_the_apply_outright()
    {
        var decision = SavegameHoldRules.DecideApply([Hold()], _otherProfileId, revision: null);

        Assert.Equal(SavegameApplyRefusal.AnotherProfileIsHeld, decision.Refusal);
        Assert.Equal(_profileId, decision.ProfileId);
    }

    /// <summary>
    /// A savegame following no mod list claims nothing about the folder, so nothing about the folder
    /// can be wrong for it - and it is not counted against the limit either.
    /// </summary>
    [Fact]
    public void A_savegame_with_no_profile_takes_no_part_in_any_of_it()
    {
        var held = new[] { Hold(noProfile: true, target: 4) };

        Assert.Null(SavegameHoldRules.RequiredRevision(held, _profileId));
        Assert.True(SavegameHoldRules.DecideApply(held, _profileId, 1004).IsAllowed);
        Assert.Null(SavegameHoldRules.FindConflictingHold(held, Guid.NewGuid()));
    }

    [Fact]
    public void A_savegame_claiming_the_mod_folder_blocks_a_second_one()
    {
        var blocking = SavegameHoldRules.FindConflictingHold([Hold()], Guid.NewGuid());

        Assert.NotNull(blocking);
        Assert.Equal(_savegameId, blocking.Value.SavegameId);
    }

    /// <summary>
    /// Checking out something this game already holds moves it to another slot rather than making
    /// it two - so it is not its own conflict.
    /// </summary>
    [Fact]
    public void A_savegame_does_not_block_itself()
    {
        Assert.Null(SavegameHoldRules.FindConflictingHold([Hold()], _savegameId));
    }

    /// <summary>
    /// Whichever profile it follows, and whether it is in compatibility mode or not: deactivating takes the game off
    /// its mod list altogether, so every savegame that claims one is in the way.
    /// </summary>
    [Fact]
    public void Any_savegame_with_a_profile_stops_a_game_being_taken_off_its_profile()
    {
        Assert.Equal(_savegameId, SavegameHoldRules.FindProfileHold([Hold()])?.SavegameId);
        Assert.Equal(_savegameId, SavegameHoldRules.FindProfileHold([Hold(target: 4)])?.SavegameId);
    }

    [Fact]
    public void A_savegame_with_no_profile_does_not_stop_a_game_being_taken_off_its_profile()
    {
        Assert.Null(SavegameHoldRules.FindProfileHold([]));
        Assert.Null(SavegameHoldRules.FindProfileHold([Hold(noProfile: true)]));
    }

    /// <summary>
    /// The required revision is per profile: a hold on one list says nothing about what the folder
    /// should be on for another, and the apply table refuses that combination anyway.
    /// </summary>
    [Fact]
    public void A_hold_on_one_profile_pins_no_revision_of_another()
    {
        Assert.Null(SavegameHoldRules.RequiredRevision([Hold(target: 4)], _otherProfileId));
    }


    [Fact]
    public void A_published_savegame_on_the_profile_the_game_is_on_may_be_kept_beside_nothing()
    {
        Assert.True(SavegameHoldRules.DecideKeepPublished([], new(_repoId, _profileId), _repoId, _profileId).IsReady);
    }

    [Fact]
    public void A_published_savegame_with_no_profile_may_always_be_kept()
    {
        Assert.True(SavegameHoldRules.DecideKeepPublished([Hold()], null, _repoId, null).IsReady);
    }

    [Fact]
    public void A_published_savegame_on_a_profile_the_game_is_not_on_activates_it_first()
    {
        Assert.Equal(new SavegameKeepPlan(null, true), SavegameHoldRules.DecideKeepPublished([], new(_repoId, _otherProfileId), _repoId, _profileId));
        Assert.Equal(new SavegameKeepPlan(null, true), SavegameHoldRules.DecideKeepPublished([], null, _repoId, _profileId));

        // The same profile id in another repo's active profile is not this profile.
        Assert.Equal(new SavegameKeepPlan(null, true), SavegameHoldRules.DecideKeepPublished([], new(Guid.NewGuid(), _profileId), _repoId, _profileId));
    }

    /// <summary>On the game's own profile too: one mod folder holds one savegame that follows a profile.</summary>
    [Fact]
    public void A_published_savegame_kept_beside_another_that_follows_a_profile_checks_that_one_in_first()
    {
        var held = Hold();

        Assert.Equal(new SavegameKeepPlan(held.SavegameId, false), SavegameHoldRules.DecideKeepPublished([held], new(_repoId, _profileId), _repoId, _profileId));
        Assert.True(SavegameHoldRules.DecideKeepPublished([Hold(noProfile: true)], new(_repoId, _profileId), _repoId, _profileId).IsReady);
    }

    /// <summary>
    /// The savegame held here follows the profile the game is on, and the new one follows another:
    /// the held one is checked in to free the folder, and the other profile is activated.
    /// </summary>
    [Fact]
    public void A_published_savegame_on_another_profile_than_a_held_one_needs_both_steps()
    {
        var held = Hold();

        Assert.Equal(
            new SavegameKeepPlan(held.SavegameId, true),
            SavegameHoldRules.DecideKeepPublished([held], new(_repoId, _profileId), _repoId, _otherProfileId));
    }


    /// <param name="target">A number puts it in compatibility mode, pinned to that revision; null follows head.</param>
    private static SavegameCheckoutBinding Hold(int? target = null, bool noProfile = false)
        => new(Guid.NewGuid(), _savegameId, Keys.Slot("savegame1"), 1, "aaaa", DateTime.UtcNow)
        {
            ProfileId = noProfile ? null : _profileId,
            ProfileRevision = target ?? 1,
            TargetRevision = target
        };
}
