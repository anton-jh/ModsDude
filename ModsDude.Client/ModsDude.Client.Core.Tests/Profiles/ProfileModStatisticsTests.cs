using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileModStatisticsTests
{
    [Fact]
    public void A_profile_adds_up_its_mods_and_their_sizes()
    {
        var statistics = ProfileModStatistics.From([Dependency("a", 100), Dependency("b", 250)]);

        Assert.Equal(new ProfileModStatistics(2, 350), statistics);
    }

    [Fact]
    public void An_empty_profile_has_nothing_to_add_up()
    {
        Assert.Equal(new ProfileModStatistics(0, 0), ProfileModStatistics.From([]));
    }


    private static ModDependencyDto Dependency(string modId, long sizeBytes) => new()
    {
        ModId = modId,
        ModVersionId = "1.0.0",
        FileName = $"{modId}.zip",
        ContentHash = modId,
        SizeBytes = sizeBytes,
        Locked = false
    };
}
