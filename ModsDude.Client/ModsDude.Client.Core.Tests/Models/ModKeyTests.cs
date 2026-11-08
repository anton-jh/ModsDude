using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Models;

/// <summary>
/// Both keys become segments of a blob name on the server, which refuses anything that is not
/// exactly one segment. Refusing the same here keeps such a mod out of the catalog instead of
/// failing it at upload.
/// </summary>
public class ModKeyTests
{
    [Theory]
    [InlineData("FS25_MyMod")]
    [InlineData("  fs25_mymod ")]
    public void An_ordinary_id_is_valid(string raw)
    {
        Assert.True(ModKey.IsValid(raw));
        Assert.Equal("fs25_mymod", ModKey.From(raw).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("mod.")]
    [InlineData("a\tb")]
    public void An_id_that_is_not_one_segment_is_refused(string raw)
    {
        Assert.False(ModKey.IsValid(raw));
        Assert.Throws<ArgumentException>(() => ModKey.From(raw));
    }

    [Theory]
    [InlineData("1.0.0.0")]
    [InlineData("2024.08.09.1345")]
    public void An_ordinary_version_is_valid(string raw)
    {
        Assert.True(ModVersionKey.IsValid(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.0.")]
    [InlineData("1.0/beta")]
    [InlineData("..")]
    public void A_version_that_is_not_one_segment_is_refused(string raw)
    {
        Assert.False(ModVersionKey.IsValid(raw));
        Assert.Throws<ArgumentException>(() => ModVersionKey.From(raw));
    }

    [Fact]
    public void A_version_longer_than_the_server_accepts_is_refused()
    {
        Assert.False(ModVersionKey.IsValid(new string('1', 129)));
    }
}
