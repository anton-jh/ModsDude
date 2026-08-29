using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Tests.Imagery;

public class ModImageReferenceLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(29)]
    public void A_position_says_which_gallery_entry_and_which_rendition_it_is(int index)
    {
        var full = ModImageReferenceLayout.GetPosition(index, ModImageDerivative.Full);
        var thumbnail = ModImageReferenceLayout.GetPosition(index, ModImageDerivative.Thumbnail);

        Assert.Equal(index, ModImageReferenceLayout.GetIndex(full));
        Assert.Equal(index, ModImageReferenceLayout.GetIndex(thumbnail));
        Assert.Equal(ModImageDerivative.Full, ModImageReferenceLayout.GetDerivative(full));
        Assert.Equal(ModImageDerivative.Thumbnail, ModImageReferenceLayout.GetDerivative(thumbnail));
    }

    [Fact]
    public void No_two_renditions_of_a_kind_claim_the_same_position()
    {
        // The server refuses a set where two images of a kind collide, and a version's imagery is
        // arrived at by more than one uploader, so the layout has to be collision-free by itself.
        var positions = Enumerable.Range(0, 30)
            .SelectMany(index => new[] { ModImageDerivative.Full, ModImageDerivative.Thumbnail }
                .Select(derivative => ModImageReferenceLayout.GetPosition(index, derivative)))
            .ToList();

        Assert.Equal(positions.Count, positions.Distinct().Count());
    }

    [Fact]
    public void Positions_order_the_gallery_the_way_it_was_built()
    {
        var ordered = Enumerable.Range(0, 5)
            .SelectMany(index => new[] { ModImageDerivative.Full, ModImageDerivative.Thumbnail }
                .Select(derivative => ModImageReferenceLayout.GetPosition(index, derivative)))
            .OrderBy(x => x)
            .Select(ModImageReferenceLayout.GetIndex)
            .Distinct();

        Assert.Equal([0, 1, 2, 3, 4], ordered);
    }

    [Fact]
    public void An_icon_is_a_thumbnail_and_nothing_else()
    {
        // Not a choice: the server accepts at most one reference of kind Icon. Pointing that one at
        // the full rendition would put ~50 KB behind every row of a cold list.
        Assert.Equal([ModImageDerivative.Thumbnail], ModImageReferenceLayout.GetDerivatives(ModImageKind.Icon));

        var reference = ModImageReferenceLayout.CreateReference(
            ModImageKind.Icon, 0, ModImageDerivative.Thumbnail, new string('a', 64), "icon.dds");

        Assert.Equal(0, reference.Position);
    }

    [Fact]
    public void A_store_image_carries_both()
    {
        Assert.Equal(
            [ModImageDerivative.Full, ModImageDerivative.Thumbnail],
            ModImageReferenceLayout.GetDerivatives(ModImageKind.StoreImage));
    }
}
