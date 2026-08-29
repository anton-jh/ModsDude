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
    public void Two_icons_are_rejected()
    {
        Assert.False(ModVersion.CheckImagesAreValid([Icon("icon.webp"), Icon("other.webp")]));
    }

    [Fact]
    public void Two_store_images_at_the_same_position_are_rejected()
    {
        Assert.False(ModVersion.CheckImagesAreValid([Store(0, "one.webp"), Store(0, "two.webp")]));
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

        Assert.Throws<InvalidOperationException>(() => version.SetImages([Icon("a.webp"), Icon("b.webp")], _later));
        Assert.Equal(["kept.webp"], version.Images.Select(x => x.FileName));
    }

    [Fact]
    public void A_reference_to_something_that_is_not_a_hash_is_rejected()
    {
        Assert.Throws<DomainValidationException>(() => new ModImageReference("not-a-hash", ModImageKind.Icon, 0, "icon.webp"));
    }


    private static ModVersion Version() => new()
    {
        RepoId = _repoId,
        ModId = _modId,
        Id = new ModVersionId("1.0.0"),
        SequenceNumber = 0,
        DisplayName = "Test mod",
        Description = "",
        ContentHash = _hash,
        Locked = false,
        Attributes = [],
        Created = _registered,
        Updated = _registered
    };

    private static ModImageReference Icon(string fileName = "icon.webp")
        => new(_hash, ModImageKind.Icon, 0, fileName);

    private static ModImageReference Store(int position, string fileName)
        => new(_hash, ModImageKind.StoreImage, position, fileName);
}
