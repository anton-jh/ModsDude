using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileModStatisticsTests
{
    [Fact]
    public void A_profile_adds_up_its_mods_and_their_sizes()
    {
        var statistics = ProfileModStatistics.From(Revision(4, Pin("a", 100), Pin("b", 250)));

        Assert.Equal(new ProfileModStatistics(4, 2, 350), statistics);
    }

    [Fact]
    public void An_empty_profile_has_nothing_to_add_up()
    {
        Assert.Equal(new ProfileModStatistics(1, 0, 0), ProfileModStatistics.From(Revision(1)));
    }


    private static GetProfileModPinsResponse Revision(int number, params ProfileModPinDto[] pins) => new()
    {
        Revision = number,
        IsHead = true,
        Pins = [.. pins]
    };

    private static ProfileModPinDto Pin(string modId, long sizeBytes) => new()
    {
        ModId = modId,
        ModVersionId = "1.0.0",
        FileName = $"{modId}.zip",
        ContentHash = modId,
        SizeBytes = sizeBytes,
        Locked = false
    };
}
