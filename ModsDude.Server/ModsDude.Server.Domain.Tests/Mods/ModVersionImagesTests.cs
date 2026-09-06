using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Tests.Mods;

public class ModVersionImagesTests
{
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly ModId _modId = new("FS25_TestMod");
    private static readonly DateTimeOffset _registered = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _later = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string _hash = new('a', ModImageHash.Length);


    [Fact]
    public void A_freshly_registered_version_has_no_images()
    {
        Assert.Empty(Version().Images);
    }

    [Fact]
    public void Setting_images_replaces_whatever_was_there()
    {
        // A retry and an opportunistic backfill both send the whole set they believe in, so the
        // second call has to be the answer rather than the union of the two.
        var version = Version();

        version.SetImages([Store(0, "one.webp"), Store(1, "two.webp")], _later);
        version.SetImages([Store(0, "only.webp")], _later);

        Assert.Equal(["only.webp"], version.Images.Select(x => x.FileName));
    }

    [Fact]
    public void Setting_images_stamps_the_version_as_updated()
    {
        // The delta form of the mod list is keyed on Updated, so imagery arriving late has to move
        // the version or no client ever learns about it.
        var version = Version();

        version.SetImages([Icon()], _later);

        Assert.Equal(_later, version.Updated);
    }

    [Fact]
    public void Clearing_the_images_is_expressible()
    {
        var version = Version();
        version.SetImages([Icon(), Store(0, "one.webp")], _later);

        version.SetImages([], _later);

        Assert.Empty(version.Images);
    }

    [Fact]
    public void An_icon_alongside_store_images_is_valid()
    {
        Assert.True(ModVersion.CheckImagesAreValid([Icon(), Store(0, "one.webp"), Store(1, "two.webp")]));
    }

    [Fact]
    public void An_icon_may_be_stored_at_both_renditions()
    {
        // The whole point of the rendition field: an icon is drawn at 64 px in a list row and large
        // in a details dialog that has no store images to show instead.
        Assert.True(ModVersion.CheckImagesAreValid([Icon(), Icon(ModImageRendition.Full)]));
    }

    [Fact]
    public void Two_icons_of_a_rendition_are_rejected()
    {
        Assert.False(ModVersion.CheckImagesAreValid(
            [Icon(fileName: "icon.webp"), Icon(fileName: "other.webp")]));
    }

    [Fact]
    public void Two_icons_at_different_positions_are_still_two_icons()
    {
        // Uniqueness on (Kind, Rendition, Position) does not catch this on its own, which is why the
        // icon rule is a rule of its own rather than folded into it.
        Assert.False(ModVersion.CheckImagesAreValid(
            [Icon(fileName: "icon.webp"), Icon(fileName: "other.webp", position: 1)]));
    }

    [Fact]
    public void Two_store_images_of_a_rendition_at_the_same_position_are_rejected()
    {
        Assert.False(ModVersion.CheckImagesAreValid([Store(0, "one.webp"), Store(0, "two.webp")]));
    }

    [Fact]
    public void The_two_renditions_of_one_store_image_share_a_position()
    {
        // They are one image, and sharing a position is what says so. Splitting them across two
        // positions is the arithmetic this field exists to replace.
        Assert.True(ModVersion.CheckImagesAreValid(
            [Store(0, "one.webp"), Store(0, "one.webp", ModImageRendition.Full)]));
    }

    [Fact]
    public void An_icon_and_a_store_image_may_share_a_position()
    {
        // Position orders a kind, not the gallery as a whole.
        Assert.True(ModVersion.CheckImagesAreValid([Icon(), Store(0, "one.webp")]));
    }

    [Fact]
    public void Setting_an_invalid_set_throws_and_leaves_the_images_alone()
    {
        var version = Version();
        version.SetImages([Store(0, "kept.webp")], _later);

        Assert.Throws<InvalidOperationException>(() => version.SetImages(
            [Icon(fileName: "a.webp"), Icon(fileName: "b.webp")], _later));
        Assert.Equal(["kept.webp"], version.Images.Select(x => x.FileName));
    }

    [Fact]
    public void A_reference_to_something_that_is_not_a_hash_is_rejected()
    {
        Assert.Throws<DomainValidationException>(() => new ModImageReference("not-a-hash", ModImageKind.Icon, ModImageRendition.Thumbnail, 0, "icon.webp"));
    }


    private static ModVersion Version() => new()
    {
        RepoId = _repoId,
        ModId = _modId,
        Id = new ModVersionId("1.0.0"),
        SequenceNumber = 0,
        DisplayName = "Test mod",
        Description = "",
        FileName = $"{_modId.Value}.zip",
        ContentHash = _hash,
        Locked = false,
        Attributes = [],
        Created = _registered,
        Updated = _registered
    };

    private static ModImageReference Icon(
        ModImageRendition rendition = ModImageRendition.Thumbnail, string fileName = "icon.webp", int position = 0)
        => new(_hash, ModImageKind.Icon, rendition, position, fileName);

    private static ModImageReference Store(
        int position, string fileName, ModImageRendition rendition = ModImageRendition.Thumbnail)
        => new(_hash, ModImageKind.StoreImage, rendition, position, fileName);
}
