using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles.Editor;
using static ModsDude.Client.Core.Tests.Keys;
using static ModsDude.Client.Core.Tests.Profiles.Editor.EditorFixtures;

namespace ModsDude.Client.Core.Tests.Profiles.Editor;

/// <summary>Dismissed versions: never offered as updates, pinned or not, and still pickable.</summary>
public class ProfileEditorDismissTests
{
    private static readonly CatalogModVersion[] _versions =
        [Registered("m", "1.0", 0), Registered("m", "1.1", 1), Registered("m", "1.2", 2)];


    [Fact]
    public void A_dismissed_version_is_not_offered_and_one_between_it_and_the_pin_is()
    {
        var draft = Draft(Pin("m", "1.0")).Dismiss([Version("m", "1.2")]);

        var state = Compute(Inputs(draft, [.. _versions]));

        Assert.Equal(V("1.1"), Assert.Single(state.Updates.Available).To);
        Assert.Equal(V("1.1"), state.Row("m").Update?.To);
    }

    [Fact]
    public void A_newer_version_than_the_dismissed_one_is_offered_again()
    {
        var draft = Draft(Pin("m", "1.1")).Dismiss([Version("m", "1.2")]);

        var before = Compute(Inputs(draft, [.. _versions]));
        var after = Compute(Inputs(draft, [.. _versions, Registered("m", "1.3", 3)]));

        Assert.Null(before.Row("m").Update);
        Assert.Equal(0, before.UpdatesFilterCount);
        Assert.Equal(V("1.3"), after.Row("m").Update?.To);
    }

    [Fact]
    public void Several_versions_of_one_mod_can_be_dismissed()
    {
        var draft = Draft(Pin("m", "1.0")).Dismiss([Version("m", "1.1"), Version("m", "1.2")]);

        var state = Compute(Inputs(draft, [.. _versions]));

        Assert.Null(state.Row("m").Update);
        Assert.False(state.Updates.HasAny);
    }

    [Fact]
    public void A_locked_mods_dismissed_version_is_not_offered_either()
    {
        var draft = Draft(Pin("m", "1.0", locked: true)).Dismiss([Version("m", "1.2")]);

        var state = Compute(Inputs(draft, [.. _versions]));

        Assert.Equal(V("1.1"), Assert.Single(state.Updates.Skipped).To);
    }

    [Fact]
    public void A_dismissed_version_stays_pickable_and_is_marked()
    {
        var draft = Draft(Pin("m", "1.0")).Dismiss([Version("m", "1.2")]);

        var options = Compute(Inputs(draft, [.. _versions])).Row("m").Options;

        Assert.Equal([V("1.2"), V("1.1"), V("1.0")], options.Select(x => x.Version.VersionId));
        Assert.Equal([true, false, false], options.Select(x => x.IsDismissed));
        Assert.Equal("1.2 · dismissed", options[0].Label);
    }

    [Fact]
    public void A_left_row_points_at_the_newest_version_not_dismissed_and_its_status_follows()
    {
        var draft = Draft().Dismiss([Version("m", "1.1")]);

        var row = Compute(Inputs(draft, Registered("m", "1.0", 0), Local("m", "1.1"))).Available.Single();

        Assert.Equal(V("1.0"), row.Version.VersionId);
        Assert.Equal(AvailableModStatus.InRepo, row.Status);
        Assert.True(row.Options[0].IsDismissed);
    }

    [Fact]
    public void A_left_row_whose_every_version_is_dismissed_points_at_the_newest()
    {
        var draft = Draft().Dismiss([Version("m", "1.0"), Version("m", "1.1")]);

        var row = Compute(Inputs(draft, Registered("m", "1.0", 0), Registered("m", "1.1", 1))).Available.Single();

        Assert.Equal(V("1.1"), row.Version.VersionId);
    }

    [Fact]
    public void A_left_rows_explicit_choice_wins_over_a_dismissal()
    {
        var inputs = Inputs(Draft().Dismiss([Version("m", "1.1")]), Registered("m", "1.0", 0), Registered("m", "1.1", 1)) with
        {
            AvailableChoices = new Dictionary<ModKey, ModVersionKey> { [Mod("m")] = V("1.1") }
        };

        Assert.Equal(V("1.1"), Compute(inputs).Available.Single().Version.VersionId);
    }

    [Fact]
    public void A_mod_taken_out_and_added_back_still_skips_its_dismissed_version()
    {
        var draft = Draft(Pin("m", "1.0")).Dismiss([Version("m", "1.2")]).Remove([Mod("m")]).Pin(Pin("m", "1.0"));

        Assert.Equal(V("1.1"), Compute(Inputs(draft, [.. _versions])).Row("m").Update?.To);
    }
}
