using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// What a savegame row's check-out has to do first: check in the savegame holding the mod folder, and
/// activate the profile.
/// </summary>
/// <remarks>
/// The engine refuses a check-out that skips either step - that is the backstop - so what is being
/// asserted is that the check-out knows to ask about them before it writes anything.
/// </remarks>
public class SavegameRowRulesTests
{
    private static readonly Guid _profileId = Guid.NewGuid();
    private static readonly Guid _otherProfileId = Guid.NewGuid();
    private static readonly Guid _savegameId = Guid.NewGuid();
    private static readonly Guid _otherSavegameId = Guid.NewGuid();


    /// <summary>
    /// The ordinary evening: a savegame checked out on latest, on a game following its profile, whose folder is on
    /// head. One click, and nothing to read.
    /// </summary>
    [Fact]
    public void On_latest_a_folder_already_at_head_is_one_click()
    {
        var offer = Describe(head: 1004, appliedRevision: 1004);

        Assert.Null(offer.ChecksInFirst);
        Assert.False(offer.ActivatesFirst);
    }

    /// <summary>
    /// The game is on this profile but behind its head, which is what a savegame on latest runs on. The
    /// revision is deliberately not named: a savegame on latest follows whatever its profile says now, so a
    /// number there would be one to memorise rather than a thing to do.
    /// </summary>
    [Fact]
    public void On_latest_a_stale_folder_activates_the_profile_by_name_first()
    {
        var offer = Describe(head: 1004, appliedRevision: 1000);

        Assert.True(offer.ActivatesFirst);
        Assert.Null(offer.ChecksInFirst);
        Assert.Equal("'Old-school'", SavegameRowRules.DescribeActivation("Old-school", offer.PinnedRevision));
    }

    [Fact]
    public void On_latest_a_game_following_another_profile_activates_it_first()
    {
        Assert.True(Describe(1004, null, _otherProfileId, 1004).ActivatesFirst);
    }

    /// <summary>
    /// A savegame in compatibility mode runs on one revision only, so the activation names it: the profile's latest is
    /// what the apply table refuses, and a question naming only "Old-school" would describe an
    /// activation that does the wrong thing.
    /// </summary>
    [Fact]
    public void Compatibility_mode_names_the_revision_it_runs_on()
    {
        var offer = Describe(head: 1004, pinned: 4, appliedRevision: 1004);

        Assert.True(offer.ActivatesFirst);
        Assert.Equal(4, offer.PinnedRevision);
        Assert.Equal("'Old-school' rev 4", SavegameRowRules.DescribeActivation("Old-school", offer.PinnedRevision));
    }

    [Fact]
    public void Compatibility_mode_on_a_folder_already_at_its_revision_is_one_click()
    {
        Assert.False(Describe(head: 1004, pinned: 4, appliedRevision: 4).ActivatesFirst);
    }

    /// <summary>
    /// Asked for even where the folder is already exactly right, since the limit is one savegame
    /// claiming a mod folder rather than one revision.
    /// </summary>
    [Fact]
    public void Another_savegame_holding_the_mod_folder_is_checked_in_first()
    {
        var offer = Describe(head: 1004, appliedRevision: 1004, held: [Hold(_otherSavegameId, _profileId)]);

        Assert.Equal(_otherSavegameId, offer.ChecksInFirst);
        Assert.False(offer.ActivatesFirst);
    }

    /// <summary>
    /// The savegame holding the folder follows another profile, so the folder is on that one: checking
    /// it in frees the folder, and the activation then moves it.
    /// </summary>
    [Fact]
    public void A_savegame_on_another_profile_is_checked_in_and_the_profile_activated()
    {
        var offer = Describe(1004, null, _otherProfileId, 7, held: [Hold(_otherSavegameId, _otherProfileId)]);

        Assert.Equal(_otherSavegameId, offer.ChecksInFirst);
        Assert.True(offer.ActivatesFirst);
    }

    /// <summary>
    /// Checking out something this game already holds moves it between slots rather than making it
    /// two, so its own binding is not checked in first.
    /// </summary>
    [Fact]
    public void A_savegame_already_held_here_is_not_checked_in_first()
    {
        Assert.Null(Describe(head: 1004, appliedRevision: 1004, held: [Hold(_savegameId, _profileId)]).ChecksInFirst);
    }

    /// <summary>
    /// A savegame following no mod list claims no folder, so nothing about the folder can be wrong for it -
    /// and nothing held there is in its way.
    /// </summary>
    [Fact]
    public void A_savegame_with_no_mod_list_checks_in_and_activates_nothing()
    {
        var offer = SavegameRowRules.Describe(
            _savegameId, profileId: null, headRevision: null, pinnedRevision: null,
            held: [Hold(_otherSavegameId, _otherProfileId)], appliedProfileId: null, appliedRevision: null);

        Assert.Null(offer.ChecksInFirst);
        Assert.False(offer.ActivatesFirst);
    }

    /// <summary>
    /// A profile this member cannot see leaves nothing able to say what the folder ought to be on, so
    /// the row claims nothing about it and lets the engine have the last word.
    /// </summary>
    [Fact]
    public void A_profile_that_cannot_be_seen_activates_nothing()
    {
        Assert.False(Describe(null, null, _otherProfileId, 9).ActivatesFirst);
    }

    /// <summary>The folder is still claimed by the savegame held there, whatever this member can see.</summary>
    [Fact]
    public void A_profile_that_cannot_be_seen_still_checks_in_what_holds_the_folder()
    {
        Assert.Equal(_otherSavegameId, Describe(null, null, _otherProfileId, 9, held: [Hold(_otherSavegameId, _otherProfileId)]).ChecksInFirst);
    }

    /// <summary>
    /// A folder nothing has ever synced is not where any savegame needs it, whatever the numbers say.
    /// </summary>
    [Fact]
    public void A_folder_that_has_never_been_synced_is_activated_first()
    {
        Assert.True(Describe(1004, null, null, null).ActivatesFirst);
    }


    /// <summary>The row against a game whose mod folder is on this savegame's own profile.</summary>
    private static SavegameRowOffer Describe(
        int? head,
        int? pinned = null,
        int? appliedRevision = null,
        IReadOnlyList<SavegameCheckoutBinding>? held = null)
        => Describe(head, pinned, _profileId, appliedRevision, held);

    /// <summary>The same, for the cases where the folder is somewhere else entirely.</summary>
    private static SavegameRowOffer Describe(
        int? head,
        int? pinned,
        Guid? appliedProfileId,
        int? appliedRevision,
        IReadOnlyList<SavegameCheckoutBinding>? held = null)
        => SavegameRowRules.Describe(
            _savegameId,
            _profileId,
            head,
            pinned,
            held ?? [],
            appliedProfileId,
            appliedRevision);

    private static SavegameCheckoutBinding Hold(Guid savegameId, Guid profileId)
        => new(Guid.NewGuid(), savegameId, Keys.Slot("savegame1"), 1, "aaaa", DateTime.UtcNow)
        {
            ProfileId = profileId,
            ProfileRevision = 1
        };
}
