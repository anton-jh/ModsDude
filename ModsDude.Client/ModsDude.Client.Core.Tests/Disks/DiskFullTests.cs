using ModsDude.Client.Core.Disks;

namespace ModsDude.Client.Core.Tests.Disks;

public class DiskFullTests
{
    [Theory]
    [InlineData(unchecked((int)0x80070070))]
    [InlineData(unchecked((int)0x80070027))]
    public void Windows_disk_full_errors_are_recognised(int hResult)
    {
        Assert.True(DiskFull.Is(new IOException("There is not enough space on the disk.", hResult)));
    }

    [Fact]
    public void A_disk_full_error_is_recognised_inside_another()
    {
        var inner = new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));

        Assert.True(DiskFull.Is(new InvalidOperationException("Fetching failed.", inner)));
    }

    [Fact]
    public void Other_errors_are_not_a_full_disk()
    {
        Assert.False(DiskFull.Is(new IOException("The file is in use.", unchecked((int)0x80070020))));
        Assert.False(DiskFull.Is(new InvalidOperationException("Something else.")));
    }
}
