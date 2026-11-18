using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Profiles.Editor;

public class DriftReviewFilterTests
{
    private static readonly DriftReport _updated = new(DriftStatus.Drifted, [], [], ["a.zip"], []);


    [Fact]
    public void A_folder_the_game_only_updated_shows_updates()
    {
        Assert.True(_updated.IsUpdatesOnly);
        Assert.Equal(PinnedModFilter.Updates, DriftReviewFilter.For(_updated));
    }

    public static TheoryData<DriftReport> MoreThanUpdates => new()
    {
        _updated with { Added = ["new.zip"] },
        _updated with { Removed = ["gone.zip"] },
        _updated with { ProfileChangedMods = [ModKey.From("x")] },
        _updated with { EditedGameFiles = ["mods.xml"] },
        _updated with { AppliedRevision = 1, CurrentRevision = 2 },
        _updated with { LockedDrift = [new(ModKey.From("a"), "A", null, LockedDriftReason.FileChanged)] },
        new DriftReport(DriftStatus.Drifted, [], [], [], []) with { AppliedRevision = 1, CurrentRevision = 2 },
        _updated with { Status = DriftStatus.NotApplied }
    };

    [Theory]
    [MemberData(nameof(MoreThanUpdates))]
    public void Anything_but_updates_shows_what_is_not_in_the_sources(DriftReport report)
    {
        Assert.False(report.IsUpdatesOnly);
        Assert.Equal(PinnedModFilter.NotInSources, DriftReviewFilter.For(report));
    }

    [Fact]
    public void Several_folders_show_updates_only_where_every_one_of_them_was_only_updated()
    {
        Assert.Equal(PinnedModFilter.Updates, DriftReviewFilter.For([_updated, _updated]));
        Assert.Equal(PinnedModFilter.NotInSources, DriftReviewFilter.For([_updated, _updated with { Removed = ["gone.zip"] }]));
    }

    [Fact]
    public void No_folders_show_what_is_not_in_the_sources()
    {
        Assert.Equal(PinnedModFilter.NotInSources, DriftReviewFilter.For(Array.Empty<DriftReport>()));
    }
}
