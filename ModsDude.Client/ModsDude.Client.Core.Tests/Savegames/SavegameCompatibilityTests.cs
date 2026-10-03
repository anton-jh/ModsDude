using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

public class SavegameCompatibilityTests
{
    private static readonly SavegameCompatibilityPolicy _policy = new(AddedWeight: 1, ChangedWeight: 2, RemovedWeight: 5, PromptThreshold: 10);


    [Fact]
    public void Each_kind_of_change_scores_its_own_weight()
    {
        var verdict = Assess(
            [Pin("A", "1.0"), Pin("B", "1.0")],
            [Pin("A", "2.0"), Pin("C", "1.0")]);

        // A changed (2), B removed (5), C added (1).
        Assert.Equal(8, verdict.Score);
        Assert.Equal(1, verdict.AddedCount);
        Assert.Equal(1, verdict.VersionChangedCount);
        Assert.Equal(1, verdict.RemovedCount);
        Assert.False(verdict.ShouldPrompt);
    }

    /// <summary>A lock toggled on its own changes no file the game reads.</summary>
    [Fact]
    public void A_lock_toggled_without_a_version_change_scores_nothing()
    {
        var verdict = Assess([Pin("A", "1.0")], [Pin("A", "1.0", lockedByProfile: true)]);

        Assert.Equal(0, verdict.Score);
        Assert.Equal(0, verdict.VersionChangedCount);
        Assert.Empty(verdict.LockedChanges);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void It_prompts_from_the_threshold_up(int added, bool prompts)
    {
        var after = Enumerable.Range(0, added).Select(x => Pin($"mod{x}", "1.0")).ToList();

        var verdict = Assess([], after);

        Assert.Equal(added, verdict.Score);
        Assert.Equal(prompts, verdict.ShouldPrompt);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_mod_locked_on_the_played_revision_whose_version_changed_always_prompts(bool byProfile, bool byAdapter)
    {
        var verdict = Assess([Pin("map", "1.0", byProfile, byAdapter)], [Pin("map", "2.0")]);

        Assert.True(verdict.ShouldPrompt);
        Assert.Equal("map", Assert.Single(verdict.LockedChanges).ModId.Value);
    }

    [Fact]
    public void A_mod_locked_on_the_played_revision_that_was_removed_always_prompts()
    {
        var verdict = Assess([Pin("map", "1.0", lockedByProfile: true)], []);

        Assert.True(verdict.ShouldPrompt);
        Assert.Single(verdict.LockedChanges);
    }

    /// <summary>
    /// The save was played while the mod was free to move, so locking it afterwards says nothing
    /// about this save.
    /// </summary>
    [Fact]
    public void A_mod_that_only_becomes_locked_does_not_force_a_prompt()
    {
        var verdict = Assess([Pin("map", "1.0")], [Pin("map", "2.0", lockedByProfile: true, lockedByAdapter: true)]);

        Assert.Empty(verdict.LockedChanges);
        Assert.False(verdict.ShouldPrompt);
    }

    [Fact]
    public void A_locked_mod_that_is_kept_as_it_was_does_not_prompt()
    {
        var verdict = Assess([Pin("map", "1.0", lockedByProfile: true)], [Pin("map", "1.0", lockedByProfile: true), Pin("B", "1.0")]);

        Assert.Empty(verdict.LockedChanges);
        Assert.False(verdict.ShouldPrompt);
    }

    /// <summary>
    /// Only the two ends matter to the save, so a mod added and removed again in between leaves them
    /// identical and scores nothing.
    /// </summary>
    [Fact]
    public void Two_identical_revisions_score_nothing()
    {
        var pins = new[] { Pin("A", "1.0"), Pin("map", "1.0", lockedByProfile: true) };

        var verdict = Assess(pins, pins);

        Assert.Equal(0, verdict.Score);
        Assert.False(verdict.ShouldPrompt);
    }


    private static SavegameCompatibilityVerdict Assess(IReadOnlyList<PinnedMod> played, IReadOnlyList<PinnedMod> latest)
        => SavegameCompatibility.Assess(ProfileRevisionComparison.Between(3, 7, played, latest), _policy);

    private static PinnedMod Pin(string modId, string versionId, bool lockedByProfile = false, bool lockedByAdapter = false)
        => new(
            new CatalogModVersion(
                ModKey.From(modId),
                ModVersionKey.From(versionId),
                modId,
                "",
                IsLocal: false,
                IsOnServer: true,
                Locked: lockedByAdapter),
            new ProfileModLock(lockedByAdapter, lockedByProfile));
}
