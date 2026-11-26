using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Tests.RemoteSources;
using static ModsDude.Client.Core.Tests.Keys;
using static ModsDude.Client.Core.Tests.Profiles.Editor.EditorFixtures;

namespace ModsDude.Client.Core.Tests.Profiles.Editor;

/// <summary>A savegame read as a source: the mods it lists, found in its own mod folder, the repo and the remote sources.</summary>
public class ProfileEditorSavegameTests
{
    private static readonly ModSource ModsFolder = new(ModSourceId.ForTarget(Target()), "Mods", @"C:\mods", ModSourceKind.Game);


    [Fact]
    public void With_only_the_savegame_on_the_left_list_is_what_it_lists_and_the_profile_does_not_pin()
    {
        var state = Compute(SaveOnly(
            Draft(Pin("pinned", "1.0")),
            Save(("pinned", "1.0"), ("registered", "1.0"), ("folder", "1.0")),
            [Registered("pinned", "1.0", 0), Registered("registered", "1.0", 0), Registered("unlisted", "1.0", 0)],
            [InFolder("folder", "1.0"), InFolder("unlistedfolder", "1.0")]));

        Assert.Equal(["folder", "registered"], state.Available.Shown());
    }

    [Fact]
    public void A_row_picks_the_version_the_save_recorded()
    {
        var state = Compute(SaveOnly(
            Draft(),
            Save(("m", "1.0")),
            [Registered("m", "1.0", 0), Registered("m", "3.0", 2)],
            [InFolder("m", "2.0")]));

        var row = Assert.Single(state.Available);
        Assert.Equal(V("1.0"), row.Version.VersionId);
        Assert.Equal(Vs("3.0", "2.0", "1.0"), row.Options.Select(x => x.Version.VersionId));
    }

    [Fact]
    public void A_row_picks_the_version_in_the_saves_mod_folder_when_the_recorded_one_is_not_found()
    {
        var state = Compute(SaveOnly(
            Draft(),
            Save(("recordedgone", "0.5"), ("unrecorded", null)),
            [Registered("recordedgone", "1.0", 0), Registered("recordedgone", "3.0", 1),
                Registered("unrecorded", "1.0", 0), Registered("unrecorded", "3.0", 1)],
            [InFolder("recordedgone", "2.0"), InFolder("unrecorded", "2.0")]));

        Assert.All(state.Available, x => Assert.Equal(V("2.0"), x.Version.VersionId));
    }

    [Fact]
    public void A_row_picks_the_newest_when_neither_the_recorded_version_nor_the_mod_folder_has_it()
    {
        var state = Compute(SaveOnly(
            Draft(),
            Save(("m", "0.5")),
            [Registered("m", "1.0", 0), Registered("m", "2.0", 1)],
            []));

        Assert.Equal(V("2.0"), Assert.Single(state.Available).Version.VersionId);
    }

    [Fact]
    public void A_selector_the_user_set_beats_the_saves_preference()
    {
        var inputs = SaveOnly(
            Draft(),
            Save(("m", "1.0")),
            [Registered("m", "1.0", 0), Registered("m", "2.0", 1)],
            []) with { AvailableChoices = new Dictionary<ModKey, ModVersionKey> { [Mod("m")] = V("2.0") } };

        Assert.Equal(V("2.0"), Assert.Single(Compute(inputs).Available).Version.VersionId);
    }

    [Fact]
    public void A_version_found_only_in_another_folder_is_not_what_the_save_offers()
    {
        var save = Save(("m", "1.0"));
        var catalog = Snapshot([], [Local("m", "1.0", Downloads)]);

        var state = Compute(new ProfileEditorInputs(Draft(), catalog, Now) { IncludeRegistered = false, SavegameSources = [save] });

        Assert.Empty(state.Available);
        Assert.Equal([Mod("m")], save.Match(catalog).NotFound);
    }

    [Fact]
    public void A_remote_offer_is_found_and_says_where_it_downloads_from()
    {
        var source = new FakeRemoteModSource("hub", "Hub");
        var remote = FakeRemoteModSource.Version("m", "2.0");

        var inputs = SaveOnly(Draft(), Save(("m", "2.0")), [Registered("m", "1.0", 0)], []);
        inputs = inputs with { Catalog = inputs.Catalog.WithRemote([new RemoteSourceAnswers(source, false, [remote], [remote])], _ => null) };

        var row = Assert.Single(Compute(inputs).Available);
        Assert.Equal(V("2.0"), row.Version.VersionId);
        Assert.Equal("My farm → Hub", row.Sources);
    }

    [Fact]
    public void A_row_says_which_save_lists_it_and_where_its_file_is()
    {
        var state = Compute(SaveOnly(
            Draft(),
            Save(("folder", "1.0"), ("registered", "1.0")),
            [Registered("registered", "1.0", 0)],
            [InFolder("folder", "1.0")]));

        Assert.Equal("My farm → Mods", state.Available.Single(x => x.ModId == Mod("folder")).Sources);
        Assert.Equal("My farm → This repo", state.Available.Single(x => x.ModId == Mod("registered")).Sources);
    }

    [Fact]
    public void A_row_from_a_profile_source_says_which_profile_lists_it_and_where_its_file_is()
    {
        var other = new ProfileModSource(
            Guid.NewGuid(),
            new ModSource(ModSourceId.ForProfile(Guid.NewGuid()), "Other", "", ModSourceKind.Profile),
            [Pin("m", "1.0")]);

        var state = Compute(Inputs(Draft(), Registered("m", "1.0", 0)) with { IncludeRegistered = false, ProfileSources = [other] });

        Assert.Equal("Other → This repo", Assert.Single(state.Available).Sources);
    }

    [Fact]
    public void Not_in_the_sources_is_what_the_profile_pins_and_the_save_does_not_list()
    {
        var inputs = SaveOnly(
            Draft(Pin("listed", "1.0"), Pin("unlisted", "1.0"), Pin("listednowhere", "1.0")),
            Save(("listed", "1.0"), ("listednowhere", "1.0")),
            [Registered("listed", "1.0", 0), Registered("unlisted", "1.0", 0)],
            []) with { PinnedFilter = PinnedModFilter.NotInSources };

        Assert.Equal(["unlisted"], Compute(inputs).PinnedShown.Shown());
    }

    [Fact]
    public void The_summary_counts_where_each_found_mod_comes_from_and_names_what_was_not_found()
    {
        var save = Save(("a", "1.0"), ("b", "1.0"), ("c", "1.0"), ("gone", "1.0"), ("alsogone", null));
        var catalog = Snapshot([Registered("c", "1.0", 0)], [InFolder("a", "1.0"), InFolder("b", "1.0")]);

        var summary = save.Summarize(catalog);

        Assert.Equal(5, summary.Listed);
        Assert.Equal([new SavegameModLocationCount("Mods", 2), new SavegameModLocationCount("This repo", 1)], summary.ByLocation);
        Assert.Equal([Mod("alsogone"), Mod("gone")], summary.NotFound);
    }

    [Fact]
    public void Matching_is_the_same_whatever_order_the_save_lists_its_mods_in()
    {
        var catalog = Snapshot([Registered("a", "1.0", 0), Registered("b", "1.0", 0)], []);

        var forwards = Save(("a", "1.0"), ("b", "1.0"), ("x", null)).Match(catalog);
        var backwards = Save(("x", null), ("b", "1.0"), ("a", "1.0")).Match(catalog);

        Assert.Equal(forwards.Found, backwards.Found, MatchComparer.Instance);
        Assert.Equal(forwards.NotFound, backwards.NotFound);
    }

    [Fact]
    public void A_pin_the_save_offers_is_still_found_with_the_mod_folder_on_standby()
    {
        var draft = Draft().Pin([Pin("folder", "1.0")]);

        var state = Compute(SaveOnly(draft, Save(("folder", "1.0")), [], [InFolder("folder", "1.0")]));

        var row = state.Row("folder");
        Assert.True(row.IsPending);
        Assert.Equal(ModsFolder.Id, Assert.Single(row.Version.FoundIn).Source.Id);
        Assert.False(row.NotInSources);
    }


    private static SavegameModSource Save(params (string Mod, string? Version)[] mods)
        => new(
            new ModSource(ModSourceId.ForSavegame(Game(), Slot("savegame1")), "My farm", "", ModSourceKind.Savegame),
            ModsFolder.Id,
            [.. mods.Select(x => new SavegameModReference(Mod(x.Mod), x.Version is null ? null : V(x.Version)))]);

    /// <summary>The save is the only source on: the repo and the mod folder chips are off, so the folder is on standby.</summary>
    private static ProfileEditorInputs SaveOnly(
        ProfileDraft draft,
        SavegameModSource save,
        IReadOnlyList<CatalogModVersion> registered,
        IReadOnlyList<CatalogModVersion> inFolder)
        => new(draft, Snapshot(registered, inFolder), Now) { IncludeRegistered = false, SavegameSources = [save] };

    /// <summary>
    /// A catalog whose enabled half is the registered versions and whose known half adds what is on
    /// standby, merged by version the way the real catalog merges them.
    /// </summary>
    private static ProfileEditorCatalog Snapshot(IReadOnlyList<CatalogModVersion> registered, IReadOnlyList<CatalogModVersion> standby)
    {
        var known = registered
            .Select(x => standby.FirstOrDefault(y => y.Identity == x.Identity) is CatalogModVersion local
                ? x with { IsLocal = true, FoundIn = local.FoundIn }
                : x)
            .Concat(standby.Where(x => registered.All(y => y.Identity != x.Identity)))
            .ToList();

        return new ProfileEditorCatalog(new ModCatalogSnapshot(registered, known, []), DefaultModVersionComparer.Instance);
    }

    private static CatalogModVersion InFolder(string modId, string version) => Local(modId, version, ModsFolder);


    private sealed class MatchComparer : IEqualityComparer<SavegameModMatch>
    {
        public static MatchComparer Instance { get; } = new();

        public bool Equals(SavegameModMatch? x, SavegameModMatch? y)
            => x is not null && y is not null && x.ModId == y.ModId && x.Preferred == y.Preferred && x.Versions.SequenceEqual(y.Versions);

        public int GetHashCode(SavegameModMatch obj) => obj.ModId.GetHashCode();
    }
}
