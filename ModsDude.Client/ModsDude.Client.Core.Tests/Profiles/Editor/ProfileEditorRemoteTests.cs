using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Tests.RemoteSources;
using static ModsDude.Client.Core.Tests.Keys;
using static ModsDude.Client.Core.Tests.Profiles.Editor.EditorFixtures;

namespace ModsDude.Client.Core.Tests.Profiles.Editor;

/// <summary>Versions a remote source offers, as ordinary versions in both lists.</summary>
public class ProfileEditorRemoteTests
{
    private readonly FakeRemoteModSource _source = new("hub", "Hub");


    [Fact]
    public void An_offer_is_an_update_that_downloads_on_save()
    {
        var state = Compute(Inputs(Draft(Pin("map", "1.0")), Registered("map", "1.0", 0)).WithRemote(Answers(true, "map", "1.1")));

        var update = Assert.Single(state.Updates.Available);
        Assert.Equal(V("1.1"), update.To);
        Assert.True(update.ImportsOnSave);
        Assert.Equal("Hub", update.DownloadsFrom);
        Assert.Equal(1, state.Updates.DownloadCount);
        Assert.Equal(["Hub"], state.Updates.DownloadSources);
    }

    [Fact]
    public void An_offer_can_be_picked_in_a_pinned_rows_selector()
    {
        var state = Compute(Inputs(Draft(Pin("map", "1.0")), Registered("map", "1.0", 0)).WithRemote(Answers(true, "map", "1.1")));

        var options = state.Row("map").Options;

        Assert.Equal(Vs("1.1", "1.0"), options.Select(x => x.Version.VersionId));
        Assert.Equal("1.1 · Hub", options[0].Label);
        Assert.Equal("Downloads from Hub and imports on save.", options[0].Note);
    }

    [Fact]
    public void A_pinned_offer_is_pending_and_counted_as_a_download()
    {
        var draft = Draft(Pin("map", "1.0")).SetVersion(Mod("map"), V("1.1"));

        var state = Compute(Inputs(draft, Registered("map", "1.0", 0)).WithRemote(Answers(true, "map", "1.1")));

        Assert.True(state.Row("map").IsPending);
        Assert.Equal(1, state.PendingCount);
        Assert.Equal(1, state.DownloadCount);
        Assert.Same(_source, state.Row("map").Version.Download!.Source);
    }

    [Fact]
    public void A_pinned_offer_still_resolves_with_its_source_switched_off()
    {
        var draft = Draft(Pin("map", "1.0")).SetVersion(Mod("map"), V("1.1"));

        var state = Compute(Inputs(draft, Registered("map", "1.0", 0)).WithRemote(Answers(false, "map", "1.1")));

        var row = state.Row("map");
        Assert.NotNull(row.Version.Download);
        Assert.False(row.Version.IsOnServer);
        Assert.True(row.IsPending);
        Assert.Null(row.Update);
    }

    [Fact]
    public void An_offer_appears_in_the_left_list_for_a_mod_the_profile_does_not_pin()
    {
        var state = Compute(Inputs(Draft(), Registered("map", "1.0", 0)).WithRemote(Answers(true, "map", "1.1")));

        var row = Assert.Single(state.Available);
        Assert.Equal(V("1.1"), row.Version.VersionId);
        Assert.Equal(AvailableModStatus.NewVersion, row.Status);
        Assert.Equal("Hub", row.Sources);
    }

    [Fact]
    public void With_everything_else_switched_off_the_left_list_is_what_the_remote_source_has_newer()
    {
        var state = Compute(Inputs(Draft(), Registered("map", "1.0", 0), Registered("other", "1.0", 0))
            .WithRemote(Answers(true, "map", "1.1")) with { IncludeRegistered = false });

        Assert.Equal(["map"], state.Available.Shown());
    }

    [Fact]
    public void A_pin_on_a_link_known_to_hold_something_else_is_flagged()
    {
        var remote = FakeRemoteModSource.Version("map", "1.1");
        var note = new RemoteDownloadNote(remote.DownloadUrl, remote.Identity, new ModVersionIdentity(Mod("map"), V("1.0.5")));
        var draft = Draft(Pin("map", "1.0")).SetVersion(Mod("map"), V("1.1"));

        var inputs = Inputs(draft, Registered("map", "1.0", 0));
        inputs = inputs with
        {
            Catalog = inputs.Catalog.WithRemote([new RemoteSourceAnswers(_source, true, [remote], [remote])], _ => note)
        };

        var state = Compute(inputs);

        Assert.True(state.Row("map").IsBrokenDownload);
        Assert.Empty(state.Updates.Available);
    }


    private RemoteSourceAnswers Answers(bool enabled, string modId, string version)
    {
        var remote = FakeRemoteModSource.Version(modId, version);

        return new RemoteSourceAnswers(_source, enabled, [remote], [remote]);
    }
}
