using ModsDude.Client.Core.Imagery;
using System.Text;

namespace ModsDude.Client.Core.Tests.Imagery;

public class ModImageHashingTests
{
    /// <summary>The SHA-256 of the empty input, which pins the algorithm and the casing at once.</summary>
    private const string _emptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";


    [Fact]
    public void An_address_is_a_lowercase_hex_sha256_of_the_bytes()
    {
        var hash = ModImageHashing.Compute([]);

        Assert.Equal(_emptyHash, hash);
        Assert.True(ModImageHashing.IsValidHash(hash));
    }

    [Fact]
    public void Identical_bytes_get_the_same_address()
    {
        // Which is the whole point: releases of one mod reuse their artwork, so keying by content
        // collapses them to one blob.
        var first = ModImageHashing.Compute(Encoding.UTF8.GetBytes("the same picture"));
        var second = ModImageHashing.Compute(Encoding.UTF8.GetBytes("the same picture"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_bytes_get_different_addresses()
    {
        var first = ModImageHashing.Compute(Encoding.UTF8.GetBytes("one picture"));
        var second = ModImageHashing.Compute(Encoding.UTF8.GetBytes("another picture"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Verification_accepts_the_bytes_the_address_names()
    {
        var bytes = Encoding.UTF8.GetBytes("a thumbnail");

        Assert.True(ModImageHashing.Verify(ModImageHashing.Compute(bytes), bytes));
    }

    [Fact]
    public void Verification_rejects_anything_else()
    {
        var hash = ModImageHashing.Compute(Encoding.UTF8.GetBytes("a thumbnail"));

        Assert.False(ModImageHashing.Verify(hash, Encoding.UTF8.GetBytes("something else")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")]
    [InlineData("../../etc/passwd")]
    public void Anything_that_is_not_an_address_is_refused(string? hash)
    {
        // The value is a path segment in an address space shared by every repo, so it is checked
        // rather than taken on trust.
        Assert.False(ModImageHashing.IsValidHash(hash));
    }
}
