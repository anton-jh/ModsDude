using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Profiles.Editor;
using static ModsDude.Client.Core.Tests.Keys;
using static ModsDude.Client.Core.Tests.Profiles.Editor.EditorFixtures;

namespace ModsDude.Client.Core.Tests.Profiles.Editor;

public class ProfileEditorStateTests
{
    [Fact]
    public void Updating_two_mods_at_once_moves_both_pins_and_keeps_both_rows()
    {
        var draft = Draft(Pin("a", "1.0"), Pin("b", "1.0"));
        var catalog = new[] { Registered("a", "1.0", 0), Registered("b", "1.0", 0), Local("a", "1.1"), Local("b", "1.1") };

        var before = Compute(Inputs(draft, catalog));
        var updated = draft.Pin(before.Updates.Available.Select(x => Pin(x.ModId.Value, x.To.Value)));
        var after = Compute(Inputs(updated, catalog));

        Assert.Equal(["a", "b"], after.PinnedShown.Shown());
        Assert.All(after.Pinned, x => Assert.Equal(V("1.1"), x.Version.VersionId));
        Assert.All(after.Pinned, x => Assert.Equal(ProfileModTouch.VersionChanged, x.Touch));
        Assert.All(after.Pinned, x => Assert.True(x.IsPending));
        Assert.Empty(after.Available);
        Assert.False(after.Updates.HasAny);
    }

    [Fact]
    public void The_left_list_leaves_out_what_the_profile_pins_and_what_it_held()
    {
        var draft = Draft(Pin("kept", "1.0"), Pin("taken", "1.0")).Remove([Mod("taken")]);

        var state = Compute(Inputs(draft,
            Registered("kept", "1.0", 0), Registered("kept", "2.0", 1),
            Registered("taken", "1.0", 0),
            Registered("other", "1.0", 0)));

        Assert.Equal(["other"], state.Available.Shown());
    }

    [Fact]
    public void A_left_row_points_at_the_newest_offered_version_unless_its_selector_was_set()
    {
        var catalog = new[] { Registered("m", "1.0", 0), Registered("m", "2.0", 1) };

        var byDefault = Compute(Inputs(Draft(), catalog));
        var chosen = Compute(Inputs(Draft(), catalog) with { AvailableChoices = new Dictionary<ModKey, ModVersionKey> { [Mod("m")] = V("1.0") } });
        var gone = Compute(Inputs(Draft(), catalog) with { AvailableChoices = new Dictionary<ModKey, ModVersionKey> { [Mod("m")] = V("0.5") } });

        Assert.Equal(V("2.0"), byDefault.Available.Single().Version.VersionId);
        Assert.Equal(V("1.0"), chosen.Available.Single().Version.VersionId);
        Assert.Equal(V("2.0"), gone.Available.Single().Version.VersionId);
        Assert.Equal([V("2.0"), V("1.0")], byDefault.Available.Single().Options.Select(x => x.Version.VersionId));
    }

    [Fact]
    public void With_the_repo_switched_off_the_left_list_is_what_the_folders_hold()
    {
        var state = Compute(Inputs(Draft(), Registered("registered", "1.0", 0), Local("local", "1.0")) with { IncludeRegistered = false });

        Assert.Equal(["local"], state.Available.Shown());
    }

    [Fact]
    public void A_newer_version_than_the_repo_holds_is_a_new_version()
    {
        var state = Compute(Inputs(Draft(), Registered("m", "1.0", 0), Local("m", "1.1")));

        Assert.Equal(AvailableModStatus.NewVersion, state.Available.Single().Status);
    }

    [Fact]
    public void Taken_out_mods_show_under_all_and_changes_and_not_in_the_result()
    {
        var draft = Draft(Pin("kept", "1.0"), Pin("taken", "1.0")).Remove([Mod("taken")]);
        var catalog = new[] { Registered("kept", "1.0", 0), Registered("taken", "1.0", 0) };

        ProfileEditorState With(PinnedModFilter filter) => Compute(Inputs(draft, catalog) with { PinnedFilter = filter });

        Assert.Equal(["kept", "taken"], With(PinnedModFilter.All).PinnedShown.Shown());
        Assert.Equal(["kept"], With(PinnedModFilter.Result).PinnedShown.Shown());
        Assert.Equal(["taken"], With(PinnedModFilter.Changes).PinnedShown.Shown());
        Assert.Equal(ProfileModTouch.TakenOut, With(PinnedModFilter.All).Row("taken").Touch);
        Assert.Equal(1, With(PinnedModFilter.All).ResultCount);
    }

    [Fact]
    public void A_taken_out_mod_is_not_an_update_or_a_lock()
    {
        var draft = Draft(Pin("m", "1.0", locked: true)).Remove([Mod("m")]);
        var catalog = new[] { Registered("m", "1.0", 0), Registered("m", "2.0", 1) };

        Assert.Empty(Compute(Inputs(draft, catalog) with { PinnedFilter = PinnedModFilter.Updates }).PinnedShown);
        Assert.Empty(Compute(Inputs(draft, catalog) with { PinnedFilter = PinnedModFilter.Locked }).PinnedShown);
        Assert.False(Compute(Inputs(draft, catalog)).Updates.HasAny);
    }

    [Fact]
    public void A_standby_source_offers_nothing_new_but_still_supplies_what_is_pinned()
    {
        var standbyOnly = Local("pinned", "2.0", Usb);
        var draft = Draft(Pin("pinned", "1.0")).SetVersion(Mod("pinned"), V("2.0"));

        var state = ProfileEditorState.Compute(new ProfileEditorInputs(draft, Catalog(
            [Registered("pinned", "1.0", 0), Registered("other", "1.0", 0)],
            [standbyOnly, Local("other", "2.0", Usb), Local("pinned", "3.0", Usb)]), Now));

        var row = state.Row("pinned");

        Assert.True(row.IsPending);
        Assert.Equal(Usb.Id, Assert.Single(row.Version.FoundIn).Source.Id);
        Assert.Equal([V("2.0"), V("1.0")], row.Options.Select(x => x.Version.VersionId));
        Assert.Null(row.Update);
        Assert.Equal(V("1.0"), state.Available.Single().Version.VersionId);
    }

    [Fact]
    public void Unloading_a_source_names_the_pins_nothing_else_can_supply()
    {
        var draft = Draft().Pin([Pin("only", "1.0"), Pin("registered", "1.0"), Pin("both", "1.0")]);

        var catalog = Catalog(
        [
            Local("only", "1.0", Usb),
            Registered("registered", "1.0", 0) with { IsLocal = true, FoundIn = Local("registered", "1.0", Usb).FoundIn },
            Local("both", "1.0", Usb) with { FoundIn = [.. Local("both", "1.0", Usb).FoundIn, .. Local("both", "1.0", Downloads).FoundIn] }
        ]);

        Assert.Equal([Mod("only")], catalog.PinsOnlyIn(draft, Usb.Id));
    }

    [Fact]
    public void Ignored_mods_are_hidden_until_shown_and_counted_either_way()
    {
        var draft = Draft().SetIgnored([Mod("noise")], true);
        var catalog = new[] { Registered("noise", "1.0", 0), Registered("wanted", "1.0", 0) };

        var hidden = Compute(Inputs(draft, catalog));
        var shown = Compute(Inputs(draft, catalog) with { ShowIgnored = true });

        Assert.Equal(["wanted"], hidden.AvailableShown.Shown());
        Assert.Equal(1, hidden.IgnoredCount);
        Assert.Equal(1, hidden.AvailableTotal);
        Assert.Equal(["noise", "wanted"], shown.AvailableShown.Shown());
        Assert.Equal(2, shown.AvailableTotal);
    }

    [Fact]
    public void A_saved_pin_keeps_its_date_until_the_draft_moves_it()
    {
        var added = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var draft = Draft(Pin("same", "1.0"), Pin("moved", "1.0")).SetVersion(Mod("moved"), V("2.0"));

        var state = Compute(Inputs(draft,
            Registered("same", "1.0", 0), Registered("moved", "1.0", 0), Registered("moved", "2.0", 1)) with
        {
            SavedDates = new Dictionary<ModKey, SavedPinDate>
            {
                [Mod("same")] = new(V("1.0"), added),
                [Mod("moved")] = new(V("1.0"), added)
            },
            PinnedSort = ModListSort.Default(ModListSortKind.Date)
        });

        Assert.Equal(added, state.Row("same").Added);
        Assert.Null(state.Row("moved").Added);
        Assert.Equal("Unsaved", state.Row("moved").SortCaption);
        Assert.Equal(["moved", "same"], state.PinnedShown.Shown());
    }

    [Fact]
    public void The_left_date_sort_is_when_the_version_was_imported_with_unregistered_ones_newest()
    {
        var state = Compute(Inputs(Draft(), Registered("old", "1.0", 0), Registered("recent", "1.0", 5), Local("unregistered", "1.0")) with
        {
            AvailableSort = ModListSort.Default(ModListSortKind.Date)
        });

        Assert.Equal(["unregistered", "recent", "old"], state.AvailableShown.Shown());
    }

    [Fact]
    public void Not_in_sources_is_every_pin_no_enabled_source_offers_any_version_of()
    {
        var draft = Draft(Pin("offered", "1.0"), Pin("gone", "1.0"));

        var state = Compute(Inputs(draft, Registered("offered", "1.0", 0), Registered("gone", "1.0", 0), Local("offered", "2.0")) with
        {
            IncludeRegistered = false,
            PinnedFilter = PinnedModFilter.NotInSources
        });

        Assert.Equal(["gone"], state.PinnedShown.Shown());
    }

    [Fact]
    public void A_locked_pin_is_skipped_by_the_plan_and_the_adapters_lock_counts()
    {
        var draft = Draft(Pin("mine", "1.0", locked: true), Pin("map", "1.0"), Pin("free", "1.0"));

        var state = Compute(Inputs(draft,
            Registered("mine", "1.0", 0), Registered("mine", "2.0", 1),
            Registered("map", "1.0", 0, locked: true), Registered("map", "2.0", 1, locked: true),
            Registered("free", "1.0", 0), Registered("free", "2.0", 1)));

        Assert.Equal([Mod("free")], state.Updates.Available.Select(x => x.ModId));
        Assert.Equal(["map", "mine"], state.Updates.Skipped.Select(x => x.ModId.Value).Order());
        Assert.True(state.Row("map").Lock.ByAdapter);
    }

    [Fact]
    public void The_same_inputs_give_the_same_state()
    {
        var inputs = Inputs(Draft(Pin("a", "1.0")).Pin(Pin("b", "1.0")),
            Registered("a", "1.0", 0), Registered("a", "2.0", 1), Local("b", "1.0"), Local("c", "1.0"));

        var first = Compute(inputs);
        var second = Compute(inputs);

        Assert.Equal(first.Pinned.Select(Describe), second.Pinned.Select(Describe));
        Assert.Equal(first.Available.Select(x => (x.ModId, x.Version.VersionId, x.Status)), second.Available.Select(x => (x.ModId, x.Version.VersionId, x.Status)));

        static string Describe(PinnedModRow row) => $"{row.ModId}:{row.Version.VersionId}:{row.Touch}:{row.Update?.To}";
    }
}
