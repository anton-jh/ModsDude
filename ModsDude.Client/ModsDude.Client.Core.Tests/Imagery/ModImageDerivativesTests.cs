using ModsDude.Client.Core.Imagery;

namespace ModsDude.Client.Core.Tests.Imagery;

public class ModImageDerivativesTests
{
    [Theory]
    [InlineData(512, 512, 128, 128)]
    [InlineData(1024, 1024, 128, 128)]
    [InlineData(256, 128, 128, 64)]
    [InlineData(128, 512, 32, 128)]
    public void Thumbnail_bounds_the_longest_edge(int width, int height, int expectedWidth, int expectedHeight)
    {
        var size = ModImageDerivatives.GetTargetSize(width, height, ModImageDerivative.Thumbnail);

        Assert.Equal((expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void Full_is_a_re_encode_of_everything_the_measured_set_contains()
    {
        // Sources top out at 1024 px, so the full rendition is never a downscale in practice - the
        // saving is DDS to WebP. The cap only bites on something nobody has seen yet.
        Assert.Equal((1024, 1024), ModImageDerivatives.GetTargetSize(1024, 1024, ModImageDerivative.Full));
        Assert.Equal((512, 512), ModImageDerivatives.GetTargetSize(512, 512, ModImageDerivative.Full));
        Assert.Equal((1024, 512), ModImageDerivatives.GetTargetSize(2048, 1024, ModImageDerivative.Full));
    }

    [Theory]
    [InlineData(64, 64)]
    [InlineData(128, 96)]
    public void Neither_rendition_ever_upscales(int width, int height)
    {
        Assert.Equal((width, height), ModImageDerivatives.GetTargetSize(width, height, ModImageDerivative.Thumbnail));
        Assert.Equal((width, height), ModImageDerivatives.GetTargetSize(width, height, ModImageDerivative.Full));
    }

    [Fact]
    public void A_very_wide_image_keeps_a_shorter_edge()
    {
        var (width, height) = ModImageDerivatives.GetTargetSize(4096, 3, ModImageDerivative.Thumbnail);

        Assert.Equal(128, width);
        Assert.Equal(1, height);
    }

    [Fact]
    public void An_image_with_no_area_has_no_rendition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ModImageDerivatives.GetTargetSize(0, 64, ModImageDerivative.Thumbnail));
    }
}
