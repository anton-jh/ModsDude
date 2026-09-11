using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// The apply table, the revision a hold pins the folder to, and the one-mod-list-per-instance limit.
/// </summary>
public class SavegameHoldRulesTests
{
    private static readonly Guid _profileId = Guid.NewGuid();
    private static readonly Guid _otherProfileId = Guid.NewGuid();
    private static readonly Guid _savegameId = Guid.NewGuid();


    [Fact]
    public void An_instance_holding_nothing_constrains_nothing()
    {
        Assert.Null(SavegameHoldRules.RequiredRevision([], _profileId));
        Assert.True(SavegameHoldRules.DecideApply([], _profileId, 1004).IsAllowed);
        Assert.Null(SavegameHoldRules.FindConflictingHold([], _savegameId));
    }

    /// <summary>
    /// A current savegame follows its profile - that is what current means - so preparing the mod list
    /// before a session and then checking the savegame out has to leave the new list in place.
    /// </summary>
    [Fact]
    public void A_current_savegame_pins_nothing_and_refuses_nothing()
    {
        var held = new[] { Hold() };

        Assert.Null(SavegameHoldRules.RequiredRevision(held, _profileId));
        Assert.True(SavegameHoldRules.DecideApply(held, _profileId, 1004).IsAllowed);
    }

    [Fact]
    public void A_past_savegame_pins_the_folder_to_its_own_revision()
    {
        var held = new[] { Hold(target: 4) };

        Assert.Equal(4, SavegameHoldRules.RequiredRevision(held, _profileId));

        // Re-applying it is the whole point: that is how folder drift under a past savegame gets repaired.
        Assert.True(SavegameHoldRules.DecideApply(held, _profileId, 4).IsAllowed);
    }

    [Fact]
    public void A_past_savegame_refuses_every_other_revision_including_head()
    {
        var decision = SavegameHoldRules.DecideApply([Hold(target: 4)], _profileId, 1004);

        Assert.Equal(SavegameApplyRefusal.PastSavegameIsHeld, decision.Refusal);
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
    /// Checking out something this instance already holds moves it to another slot rather than making
    /// it two - so it is not its own conflict.
    /// </summary>
    [Fact]
    public void A_savegame_does_not_block_itself()
    {
        Assert.Null(SavegameHoldRules.FindConflictingHold([Hold()], _savegameId));
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


    /// <param name="target">A number makes it past, pinned to that revision; null makes it current.</param>
    private static SavegameCheckoutBinding Hold(int? target = null, bool noProfile = false)
        => new(Guid.NewGuid(), _savegameId, "savegame1", 1, "aaaa", DateTime.UtcNow)
        {
            ProfileId = noProfile ? null : _profileId,
            ProfileRevision = target ?? 1,
            TargetRevision = target
        };
}
