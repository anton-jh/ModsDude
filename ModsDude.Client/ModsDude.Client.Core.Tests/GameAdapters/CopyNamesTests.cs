using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Tests.GameAdapters;

public class CopyNamesTests
{
    private static readonly string[] _suffixes = [" - Copy", " - kopia"];


    [Theory]
    [InlineData("FS25_Plough (1)", "FS25_Plough")]
    [InlineData("FS25_Plough (12)", "FS25_Plough")]
    [InlineData("FS25_Plough(1)", "FS25_Plough")]
    [InlineData("FS25_Plough - Copy", "FS25_Plough")]
    [InlineData("fs25_plough - copy", "fs25_plough")]
    [InlineData("FS25_Plough - kopia (2)", "FS25_Plough")]
    [InlineData("FS25_Plough (1) - Copy", "FS25_Plough")]
    [InlineData("FS25 Plough (1)", "FS25 Plough")]
    public void A_copy_name_gives_the_original(string stem, string expected)
    {
        Assert.Equal(expected, CopyNames.OriginalStem(stem, _suffixes));
    }

    [Theory]
    [InlineData("FS25_Plough")]
    [InlineData("FS25_Plough (v2) Edition")]
    [InlineData("FS25_Plough(1)_fixed")]
    [InlineData("FS25_Plough - Kopie")]
    [InlineData("FS25_Plough (beta)")]
    [InlineData("(1)")]
    [InlineData(" - Copy")]
    [InlineData("(1) - Copy (2)")]
    public void A_name_without_a_copy_marker_or_with_nothing_else_has_no_original(string stem)
    {
        Assert.Null(CopyNames.OriginalStem(stem, _suffixes));
    }

    [Fact]
    public void Only_the_given_suffixes_mark_a_copy()
    {
        Assert.Null(CopyNames.OriginalStem("FS25_Plough - kopia", [" - Copy"]));
    }

    [Theory]
    [InlineData("%s - Copy", " - Copy")]
    [InlineData("%s - kopia", " - kopia")]
    public void A_template_with_one_leading_placeholder_gives_its_suffix(string template, string expected)
    {
        Assert.Equal(expected, ExplorerCopyName.SuffixOf(template));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("%s")]
    [InlineData("%s   ")]
    [InlineData("Copy of %s")]
    [InlineData("%s - Copy %s")]
    [InlineData("%s - Copy %d")]
    [InlineData("Kopia av %1!ls!")]
    public void A_template_of_another_shape_gives_no_suffix(string? template)
    {
        Assert.Null(ExplorerCopyName.SuffixOf(template));
    }

    [Fact]
    public void Windows_has_a_copy_template_this_reads()
    {
        Assert.NotNull(ExplorerCopyName.SuffixOf(ExplorerCopyName.ReadTemplate()));
    }
}
