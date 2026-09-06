using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Tests.Helpers;

/// <summary>
/// The one comparer every display-name sort in the app runs through, so the property worth pinning
/// is the one it exists for: digits in a name are a number.
/// </summary>
public class NaturalOrderTests
{
    [Fact]
    public void A_ten_sorts_after_a_nine()
    {
        string[] names = ["10 mod", "9 mod", "1 mod"];

        Assert.Equal(["1 mod", "9 mod", "10 mod"], names.OrderBy(x => x, NaturalOrder.Comparer));
    }

    /// <summary>The shape a mod list actually has: a common stem and a number at the end.</summary>
    [Fact]
    public void A_trailing_number_orders_numerically()
    {
        string[] names = ["Pack 2", "Pack 11", "Pack 1"];

        Assert.Equal(["Pack 1", "Pack 2", "Pack 11"], names.OrderBy(x => x, NaturalOrder.Comparer));
    }

    [Fact]
    public void Case_does_not_separate_two_names()
    {
        Assert.Equal(0, NaturalOrder.Compare("FS25_MyMod", "fs25_mymod"));
    }

    /// <summary>
    /// Version strings go through the same comparer wherever they are shown rather than ordered, and
    /// dotted numerics are exactly where an ordinal sort reads worst.
    /// </summary>
    [Fact]
    public void Dotted_version_strings_order_by_their_parts()
    {
        string[] versions = ["1.10.0", "1.9.0", "1.2.0"];

        Assert.Equal(["1.2.0", "1.9.0", "1.10.0"], versions.OrderBy(x => x, NaturalOrder.Comparer));
    }
}
