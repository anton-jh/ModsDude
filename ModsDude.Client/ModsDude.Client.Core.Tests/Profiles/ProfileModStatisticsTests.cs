using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileModStatisticsTests
{
    [Fact]
    public void A_profile_adds_up_its_mods_and_their_sizes()
    {
        var statistics = ProfileModStatistics.From([Dependency("a", 100), Dependency("b", 250)]);

        Assert.Equal(new ProfileModStatistics(2, 350, 0), statistics);
        Assert.True(statistics.IsSizeComplete);
    }

    /// <summary>
    /// An unknown size is not an empty file: leaving it out of the sum silently would make the total
    /// look complete when it is a lower bound.
    /// </summary>
    [Fact]
    public void A_mod_without_a_recorded_size_is_counted_as_a_mod_but_not_as_bytes()
    {
        var statistics = ProfileModStatistics.From([Dependency("a", 100), Dependency("b", null)]);

        Assert.Equal(new ProfileModStatistics(2, 100, 1), statistics);
        Assert.False(statistics.IsSizeComplete);
    }

    [Fact]
    public void An_empty_profile_has_nothing_to_add_up()
    {
        Assert.Equal(new ProfileModStatistics(0, 0, 0), ProfileModStatistics.From([]));
    }


    private static ModDependencyDto Dependency(string modId, long? sizeBytes) => new()
    {
        ModId = modId,
        ModVersionId = "1.0.0",
        FileName = $"{modId}.zip",
        ContentHash = modId,
        SizeBytes = sizeBytes,
        Locked = false
    };
}
