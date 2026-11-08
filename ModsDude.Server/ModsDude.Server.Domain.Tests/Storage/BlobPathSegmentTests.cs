using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Domain.Tests.Storage;

/// <summary>
/// Mod ids and versions are interpolated into blob names, so what this refuses is what would let one
/// repo's member address another repo's blobs.
/// </summary>
public class BlobPathSegmentTests
{
    [Theory]
    [InlineData("1.0.0.0")]
    [InlineData("fs25_a")]
    [InlineData("2024.05.01.1230")]
    [InlineData("v1 beta")]
    public void An_ordinary_id_or_version_is_valid(string value)
    {
        Assert.True(BlobPathSegment.IsValid(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("1.0.")]
    [InlineData("../other")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData(" 1.0")]
    [InlineData("1.0 ")]
    [InlineData("1.0\n")]
    [InlineData("1.\u00000")]
    public void A_value_that_is_not_exactly_one_segment_is_refused(string? value)
    {
        Assert.False(BlobPathSegment.IsValid(value));
    }

    [Fact]
    public void A_value_longer_than_the_maximum_is_refused()
    {
        Assert.True(BlobPathSegment.IsValid(new string('a', BlobPathSegment.MaximumLength)));
        Assert.False(BlobPathSegment.IsValid(new string('a', BlobPathSegment.MaximumLength + 1)));
    }

    [Fact]
    public void A_traversing_version_cannot_be_built()
    {
        Assert.Throws<DomainValidationException>(() => new ModVersionId("../../11111111-1111-1111-1111-111111111111/a_mod/1.0"));
    }

    [Fact]
    public void A_traversing_mod_id_cannot_be_built()
    {
        Assert.Throws<DomainValidationException>(() => new ModId(".."));
    }
}
