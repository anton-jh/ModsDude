using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Domain.Tests.Mods;

public class ModImageHashTests
{
    private const string _validHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";


    [Fact]
    public void A_lowercase_hex_sha256_is_valid()
    {
        Assert.True(ModImageHash.IsValid(_validHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b8")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85555")]
    public void A_hash_of_the_wrong_length_is_rejected(string? hash)
    {
        Assert.False(ModImageHash.IsValid(hash));
    }

    [Fact]
    public void An_uppercase_hash_is_rejected()
    {
        // One set of bytes has to have exactly one address, or dedupe and the client's permanent
        // cache-by-hash both stop holding.
        Assert.False(ModImageHash.IsValid(_validHash.ToUpperInvariant()));
    }

    [Fact]
    public void A_hash_containing_a_non_hex_character_is_rejected()
    {
        Assert.False(ModImageHash.IsValid(_validHash[..63] + "g"));
    }

    /// <summary>
    /// The value becomes a blob path segment, so anything that escapes the container's layout would
    /// address something that is not an image at all.
    /// </summary>
    [Fact]
    public void A_hash_carrying_path_separators_is_rejected()
    {
        Assert.False(ModImageHash.IsValid("../" + _validHash[3..]));
    }

    [Fact]
    public void Validating_an_invalid_hash_throws()
    {
        Assert.Throws<DomainValidationException>(() => ModImageHash.Validated("not-a-hash"));
    }

    [Fact]
    public void Validating_a_valid_hash_returns_it()
    {
        Assert.Equal(_validHash, ModImageHash.Validated(_validHash));
    }
}
