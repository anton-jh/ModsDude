using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Domain.Tests.Mods;

/// <summary>
/// The registered file name is a string one member of a repo chooses and every other member's
/// client writes a file with, so what this refuses is a security property rather than tidiness.
/// </summary>
public class ModFileNameTests
{
    private static readonly ModId _modId = new("fs25_a");


    [Theory]
    [InlineData("fs25_a.zip")]
    [InlineData("FS25_A.zip")]
    [InlineData("Fs25_A.ZIP")]
    [InlineData("fs25_a")]
    public void A_bare_name_whose_stem_is_the_mod_id_is_valid(string raw)
    {
        Assert.True(ModFileName.IsValidFor(_modId, raw));
    }

    [Theory]
    [InlineData(@"..\..\Windows\System32\fs25_a.zip")]
    [InlineData("../fs25_a.zip")]
    [InlineData("mods/fs25_a.zip")]
    [InlineData(@"mods\fs25_a.zip")]
    [InlineData("C:fs25_a.zip")]
    [InlineData("fs25_a|.zip")]
    [InlineData("fs25_a.zip.")]
    [InlineData("fs25_a.zip ")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData(null)]
    public void A_name_that_is_not_a_bare_writable_file_name_is_refused(string? raw)
    {
        Assert.False(ModFileName.IsValidFor(_modId, raw));
    }

    /// <summary>
    /// The bound on what registering a name can do: respell this mod's own file, and nothing else.
    /// </summary>
    [Fact]
    public void A_name_belonging_to_another_mod_is_refused()
    {
        Assert.False(ModFileName.IsValidFor(_modId, "fs25_b.zip"));
    }

    [Fact]
    public void A_name_longer_than_a_path_component_is_refused()
    {
        Assert.False(ModFileName.IsValidFor(new ModId(new string('a', 300)), new string('a', 300) + ".zip"));
    }

    [Fact]
    public void A_name_with_no_stem_is_refused()
    {
        Assert.False(ModFileName.IsValidFor(_modId, ".zip"));
    }
}
