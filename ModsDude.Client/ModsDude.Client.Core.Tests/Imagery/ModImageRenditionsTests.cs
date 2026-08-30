using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Tests.Imagery;

public class ModImageRenditionsTests
{
    [Theory]
    [InlineData(512, 512, 128, 128)]
    [InlineData(1024, 1024, 128, 128)]
    [InlineData(256, 128, 128, 64)]
    [InlineData(128, 512, 32, 128)]
    public void Thumbnail_bounds_the_longest_edge(int width, int height, int expectedWidth, int expectedHeight)
    {
        var size = ModImageRenditions.GetTargetSize(width, height, ModImageRendition.Thumbnail);

        Assert.Equal((expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void Full_is_a_re_encode_of_everything_the_measured_set_contains()
    {
        // Sources top out at 1024 px, so the full rendition is never a downscale in practice - the
        // saving is DDS to WebP. The cap only bites on something nobody has seen yet.
        Assert.Equal((1024, 1024), ModImageRenditions.GetTargetSize(1024, 1024, ModImageRendition.Full));
        Assert.Equal((512, 512), ModImageRenditions.GetTargetSize(512, 512, ModImageRendition.Full));
        Assert.Equal((1024, 512), ModImageRenditions.GetTargetSize(2048, 1024, ModImageRendition.Full));
    }

    [Theory]
    [InlineData(64, 64)]
    [InlineData(128, 96)]
    public void Neither_rendition_ever_upscales(int width, int height)
    {
        Assert.Equal((width, height), ModImageRenditions.GetTargetSize(width, height, ModImageRendition.Thumbnail));
        Assert.Equal((width, height), ModImageRenditions.GetTargetSize(width, height, ModImageRendition.Full));
    }

    [Fact]
    public void A_very_wide_image_keeps_a_shorter_edge()
    {
        var (width, height) = ModImageRenditions.GetTargetSize(4096, 3, ModImageRendition.Thumbnail);

        Assert.Equal(128, width);
        Assert.Equal(1, height);
    }

    [Fact]
    public void An_image_with_no_area_has_no_rendition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ModImageRenditions.GetTargetSize(0, 64, ModImageRendition.Thumbnail));
    }

    [Fact]
    public void Every_image_is_published_at_both_renditions()
    {
        // Including icons, which used to get a thumbnail and nothing else because the old layout had
        // no field to tell two icon references apart.
        Assert.Equal([ModImageRendition.Full, ModImageRendition.Thumbnail], ModImageRenditions.All);
    }
}
