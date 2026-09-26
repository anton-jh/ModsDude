using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// Whether a savegame row's check-out is offered, whether it activates the profile first, and the sentence it carries where it is not.
/// </summary>
/// <remarks>
/// The point of every case here is that the refusal arrives <em>before</em> the click. The engine
/// refuses all of it anyway - that is the backstop - so what is being asserted is that the button was
/// never offered, and that what it says instead names the thing that would clear it. A mod folder on
/// the wrong list is the exception: check-out stays offered and asks to activate the profile first.
/// </remarks>
public class SavegameRowRulesTests
{
    private static readonly Guid _profileId = Guid.NewGuid();
    private static readonly Guid _otherProfileId = Guid.NewGuid();
    private static readonly Guid _savegameId = Guid.NewGuid();
    private static readonly Guid _otherSavegameId = Guid.NewGuid();


    /// <summary>
    /// The ordinary evening: a current savegame, on a game following its profile, whose folder is on
    /// head. One click, and nothing to read.
    /// </summary>
    [Fact]
    public void A_current_savegame_on_a_folder_already_at_head_is_one_click()
    {
        var offer = Describe(head: 1004, appliedRevision: 1004);

        Assert.True(offer.CanCheckOut);
        Assert.False(offer.ActivatesFirst);
        Assert.Null(SavegameRowRules.Explain(offer.CheckOut, null));
    }

    /// <summary>
    /// The game is on this profile but behind its head, which is what a current savegame runs on. The
    /// revision is deliberately not named: a current savegame follows whatever its profile says now, so a
    /// number there would be one to memorise rather than a thing to do.
    /// </summary>
    [Fact]
    public void A_current_savegame_on_a_stale_folder_activates_the_profile_by_name_first()
    {
        var offer = Describe(head: 1004, appliedRevision: 1000);

        // Offered, not refused: the check-out asks, then activates, then carries on.
        Assert.True(offer.CanCheckOut);
        Assert.True(offer.ActivatesFirst);
        Assert.Equal(SavegameRowBlock.ModFolderElsewhere, offer.CheckOut);
        Assert.Null(SavegameRowRules.Explain(offer.CheckOut, null));
        Assert.Equal("'Old-school'", SavegameRowRules.DescribeActivation("Old-school", offer.PinnedRevision));
    }

    [Fact]
    public void A_current_savegame_on_an_game_following_another_profile_activates_it_first()
    {
        var offer = Describe(1004, null, _otherProfileId, 1004);

        Assert.True(offer.CanCheckOut);
        Assert.True(offer.ActivatesFirst);
    }

    /// <summary>
    /// A past savegame runs on one revision only, so the activation names it: the profile's latest is
    /// what the apply table refuses, and a question naming only "Old-school" would describe an
    /// activation that does the wrong thing.
    /// </summary>
    [Fact]
    public void A_past_savegame_names_the_revision_it_runs_on()
    {
        var offer = Describe(head: 1004, pinned: 4, appliedRevision: 1004);

        Assert.True(offer.ActivatesFirst);
        Assert.Equal(4, offer.PinnedRevision);
        Assert.Equal("'Old-school' rev 4", SavegameRowRules.DescribeActivation("Old-school", offer.PinnedRevision));
    }

    [Fact]
    public void A_past_savegame_on_a_folder_already_at_its_revision_is_one_click()
    {
        var offer = Describe(head: 1004, pinned: 4, appliedRevision: 4);

        Assert.True(offer.CanCheckOut);
        Assert.False(offer.ActivatesFirst);
    }

    /// <summary>
    /// Checked before anything about the folder, because no activation clears it - and it holds even where
    /// the folder is already exactly right, since the limit is one savegame claiming a mod folder
    /// rather than one revision.
    /// </summary>
    [Fact]
    public void Another_savegame_holding_the_mod_folder_blocks_check_out()
    {
        var offer = Describe(head: 1004, appliedRevision: 1004, held: [Hold(_otherSavegameId)]);

        Assert.False(offer.CanCheckOut);
        Assert.False(offer.ActivatesFirst);
        Assert.Equal(_otherSavegameId, offer.BlockingSavegameId);

        Assert.Equal(
            "'Riverbend' is checked out here",
            SavegameRowRules.Explain(offer.CheckOut, "Riverbend"));
    }

    /// <summary>The refusal stands without the name, for a savegame the list is not showing.</summary>
    [Fact]
    public void A_blocking_savegame_nobody_can_name_still_refuses()
    {
        var offer = Describe(head: 1004, appliedRevision: 1004, held: [Hold(_otherSavegameId)]);

        Assert.Equal(
            "Another savegame is checked out here",
            SavegameRowRules.Explain(offer.CheckOut, null));
    }

    /// <summary>
    /// Checking out something this game already holds moves it between slots rather than making it
    /// two, so its own binding is not what stops it.
    /// </summary>
    [Fact]
    public void A_savegame_already_held_here_does_not_block_itself()
    {
        Assert.True(Describe(head: 1004, appliedRevision: 1004, held: [Hold(_savegameId)]).CanCheckOut);
    }

    /// <summary>
    /// A savegame following no mod list claims no folder, so nothing about the folder can be wrong for it.
    /// </summary>
    [Fact]
    public void A_savegame_with_no_mod_list_can_always_be_checked_out_and_activates_nothing()
    {
        var offer = SavegameRowRules.Describe(
            _savegameId, profileId: null, headRevision: null, pinnedRevision: null,
            held: [], appliedProfileId: null, appliedRevision: null);

        Assert.True(offer.CanCheckOut);
        Assert.False(offer.ActivatesFirst);
    }

    /// <summary>
    /// A profile this member cannot see leaves nothing able to say what the folder ought to be on, so
    /// the row claims nothing about it and lets the engine have the last word.
    /// </summary>
    [Fact]
    public void A_profile_that_cannot_be_seen_constrains_nothing()
    {
        var offer = Describe(null, null, _otherProfileId, 9);

        Assert.True(offer.CanCheckOut);
        Assert.False(offer.ActivatesFirst);
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

    /// <summary>The same, for the two cases where the folder is somewhere else entirely.</summary>
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

    private static SavegameCheckoutBinding Hold(Guid savegameId)
        => new(Guid.NewGuid(), savegameId, Keys.Slot("savegame1"), 1, "aaaa", DateTime.UtcNow)
        {
            ProfileId = _profileId,
            ProfileRevision = 1
        };
}
