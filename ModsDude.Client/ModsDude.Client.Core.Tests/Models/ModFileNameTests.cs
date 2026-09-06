using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Models;

/// <summary>
/// <see cref="ModFileName"/> is the one thing standing between a string another member typed onto
/// their disk and a path this machine writes to, so what it refuses matters as much as what it keeps.
/// </summary>
public class ModFileNameTests
{
    [Fact]
    public void The_casing_of_the_file_survives_a_normalized_id()
    {
        var name = ModFileName.ForFile(Keys.Mod("FS25_MyMod"), @"C:\Downloads\FS25_MyMod.zip");

        Assert.Equal("FS25_MyMod.zip", name.Value);
    }

    /// <summary>The regression this all exists for: the id is lower-cased, the file must not be.</summary>
    [Fact]
    public void A_name_differing_from_the_id_only_in_case_is_accepted()
    {
        Assert.Equal("FS25_MyMod.zip", ModFileName.For(Keys.Mod("fs25_mymod"), "FS25_MyMod.zip")?.Value);
    }

    [Theory]
    [InlineData(@"..\..\Windows\System32\fs25_a.zip")]
    [InlineData("subfolder/fs25_a.zip")]
    [InlineData(@"subfolder\fs25_a.zip")]
    [InlineData("C:fs25_a.zip")]
    [InlineData("fs25_a*.zip")]
    [InlineData("fs25_a.zip.")]
    [InlineData("fs25_a.zip ")]
    [InlineData("")]
    [InlineData(null)]
    public void A_name_that_is_not_a_bare_writable_file_name_is_refused(string? raw)
    {
        Assert.Null(ModFileName.For(Keys.Mod("fs25_a"), raw));
    }

    /// <summary>
    /// The property that bounds what a hostile repo can do with this field: it can respell a mod's
    /// own file and nothing else. Without it, one mod's registration could name another mod's file.
    /// </summary>
    [Fact]
    public void A_name_belonging_to_another_mod_is_refused()
    {
        Assert.Null(ModFileName.For(Keys.Mod("fs25_a"), "fs25_b.zip"));
    }

    [Fact]
    public void A_name_with_no_stem_is_refused()
    {
        Assert.Null(ModFileName.For(Keys.Mod("fs25_a"), ".zip"));
    }

    /// <summary>
    /// A file whose name cannot be carried still has to be registrable, and the name it falls back to
    /// is exactly what the adapter built before any of this existed.
    /// </summary>
    [Fact]
    public void An_uncarriable_name_falls_back_to_the_id_and_the_extension()
    {
        var name = ModFileName.ForFile(Keys.Mod("fs25_a"), @"C:\Downloads\fs25_a?.zip");

        Assert.Equal("fs25_a.zip", name.Value);
    }
}
