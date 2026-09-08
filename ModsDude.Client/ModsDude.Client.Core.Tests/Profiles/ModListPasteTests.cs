using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ModListPasteTests
{
    [Fact]
    public void Nothing_pasted_is_nothing_asked_for()
    {
        Assert.Empty(ModListPaste.Parse(null));
        Assert.Empty(ModListPaste.Parse("   \n  "));
    }

    [Fact]
    public void One_name_per_line()
    {
        Assert.Equal(["Foo", "Bar"], ModListPaste.Parse("Foo\nBar"));
    }

    [Fact]
    public void Commas_semicolons_and_tabs_separate_as_well_as_newlines()
    {
        Assert.Equal(["Foo", "Bar", "Baz"], ModListPaste.Parse("Foo, Bar;\tBaz"));
    }

    [Fact]
    public void Bullets_and_quotes_are_decoration()
    {
        Assert.Equal(["Foo", "Bar", "Baz"], ModListPaste.Parse("- Foo\n* \"Bar\"\n\u2022 [Baz]"));
    }

    [Fact]
    public void Numbering_goes_with_the_bullet()
    {
        Assert.Equal(["Foo", "Bar"], ModListPaste.Parse("1. Foo\n2) Bar"));
    }

    [Fact]
    public void A_name_that_starts_with_a_version_keeps_its_digits()
    {
        Assert.Equal(["1.0 Overhaul"], ModListPaste.Parse("1.0 Overhaul"));
    }

    [Fact]
    public void The_same_name_twice_is_one_thing_asked_for()
    {
        Assert.Equal(["Foo", "Bar"], ModListPaste.Parse("Foo\nBar\nfoo"));
    }

    [Fact]
    public void Order_survives_so_the_report_reads_in_the_order_it_was_pasted()
    {
        Assert.Equal(["Zeta", "Alpha"], ModListPaste.Parse("Zeta\nAlpha"));
    }
}
