using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

public class RevisionGapTests
{
    [Fact]
    public void A_folder_only_behind_its_profile_has_a_gap()
    {
        Assert.Equal(new RevisionGap(3, 6), RevisionGap.Of(Behind(3, 6)));
    }

    public static TheoryData<DriftReport> NotOnlyBehind => new()
    {
        Behind(3, 6) with { Changed = ["a.zip"] },
        Behind(3, 6) with { EditedGameFiles = ["mods.xml"] },
        Behind(3, 6) with { LockedDrift = [new(ModKey.From("a"), "A", null, LockedDriftReason.FileRemoved)] },
        Behind(3, 3),
        DriftReport.For(DriftStatus.Drifted) with { AppliedRevision = 3 },
        DriftReport.For(DriftStatus.NotApplied)
    };

    [Theory]
    [MemberData(nameof(NotOnlyBehind))]
    public void A_folder_that_drifted_any_other_way_has_no_gap(DriftReport report)
    {
        Assert.Null(RevisionGap.Of(report));
    }

    [Fact]
    public void Several_folders_compare_the_oldest_applied_revision_with_the_needed_one()
    {
        Assert.Equal(new RevisionGap(2, 6), RevisionGap.Of([Behind(4, 6), Behind(2, 6)]));
    }

    [Fact]
    public void One_folder_drifted_another_way_leaves_the_game_without_a_gap()
    {
        Assert.Null(RevisionGap.Of([Behind(4, 6), Behind(4, 6) with { Removed = ["gone.zip"] }]));
    }

    [Fact]
    public void A_drifted_savegame_leaves_the_game_without_a_gap()
    {
        var save = new SavegameDrift(Guid.NewGuid(), Guid.NewGuid(), new(new("mods"), new("slot")), SavegameDriftKind.UncheckedInPlay);

        Assert.Null(RevisionGap.Of([Behind(4, 6) with { SavegameDrift = [save] }]));
    }

    [Fact]
    public void No_drifted_folders_have_no_gap()
    {
        Assert.Null(RevisionGap.Of(Array.Empty<DriftReport>()));
    }

    [Fact]
    public void Moving_forward_is_an_update_and_moving_back_is_a_re_apply()
    {
        Assert.Equal("Update to rev 6", new RevisionGap(3, 6).ActionLabel);
        Assert.Equal("Re-apply rev 4", new RevisionGap(6, 4).ActionLabel);
    }


    private static DriftReport Behind(int applied, int current)
        => DriftReport.For(DriftStatus.Drifted) with { AppliedRevision = applied, CurrentRevision = current };
}
