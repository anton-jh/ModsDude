using ModsDude.Client.Core.ModVersions;

namespace ModsDude.Client.Core.Tests.ModVersions;

public class DefaultModVersionComparerTests
{
    private static readonly DefaultModVersionComparer _comparer = DefaultModVersionComparer.Instance;


    [Theory]
    [InlineData("1", "2")]
    [InlineData("1.2", "1.3")]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("1.2.3.4", "1.2.3.5")]
    [InlineData("1.2.3.4", "2.0.0.0")]
    [InlineData("0.9.9.9", "1.0.0.0")]
    public void Dotted_numerics_compare_segment_by_segment(string earlier, string later)
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(earlier, later));
    }

    [Theory]
    [InlineData("1.9", "1.10")]
    [InlineData("1.2.9", "1.2.10")]
    [InlineData("9.0", "10.0")]
    public void A_higher_segment_sorts_later_even_when_it_sorts_earlier_lexically(string earlier, string later)
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(earlier, later));
        Assert.Equal(ModVersionComparison.Later, _comparer.Compare(later, earlier));
    }

    [Theory]
    [InlineData("1.09", "1.10")]
    [InlineData("01.2", "01.3")]
    [InlineData("1.008", "1.010")]
    [InlineData("1.02", "1.3")]
    public void Zero_padded_segments_compare_by_value(string earlier, string later)
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(earlier, later));
    }

    [Theory]
    [InlineData("v1.2", "v1.3")]
    [InlineData("V1.2", "V1.3")]
    [InlineData("v1.2", "1.3")]
    [InlineData("1.2", "v1.3")]
    public void A_v_prefix_does_not_stop_the_numbers_deciding(string earlier, string later)
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(earlier, later));
    }

    [Theory]
    [InlineData("1.2", "1.2.1")]
    [InlineData("1", "1.0.1")]
    [InlineData("1.2.3", "1.2.3.4")]
    public void A_trailing_non_zero_segment_places_the_longer_string_later(string earlier, string later)
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(earlier, later));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v2.3.4")]
    [InlineData("1.0-beta")]
    public void A_version_equals_itself(string version)
    {
        Assert.Equal(ModVersionComparison.Equal, _comparer.Compare(version, version));
    }


    [Theory]
    [InlineData("1.0-beta", "1.0")]
    [InlineData("1.0-rc1", "1.0")]
    [InlineData("1.2b2", "1.2")]
    [InlineData("2.0_alpha", "2.0")]
    [InlineData("1.0.0.0-beta", "1.0.0.0")]
    public void A_pre_release_comes_before_its_release(string preRelease, string release)
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(preRelease, release));
        Assert.Equal(ModVersionComparison.Later, _comparer.Compare(release, preRelease));
    }

    [Theory]
    [InlineData("1.0-rc1", "1.0-rc2")]
    [InlineData("1.2b1", "1.2b2")]
    [InlineData("1.0-rc9", "1.0-rc10")]
    public void Pre_releases_sharing_a_label_compare_by_their_number(string earlier, string later)
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(earlier, later));
    }

    [Theory]
    [InlineData("1.0-rc1", "1.1")]
    [InlineData("1.0-beta", "2.0-alpha")]
    public void A_pre_release_of_an_earlier_release_still_comes_first(string earlier, string later)
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(earlier, later));
    }

    [Theory]
    [InlineData("1.0-beta", "1.0-rc1")]
    [InlineData("1.0-alpha", "1.0-beta")]
    [InlineData("1.0-rc", "1.0-final")]
    public void Two_different_pre_release_labels_are_undecidable(string left, string right)
    {
        // Alphabetical order gets beta before rc by luck and would just as confidently get rc
        // before final.
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare(left, right));
    }

    [Fact]
    public void A_labelled_pre_release_is_undecidable_against_a_numbered_one_of_the_same_label()
    {
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare("1.0-rc", "1.0-rc1"));
    }


    [Theory]
    [InlineData("v1", "1.0")]
    [InlineData("1", "1.0")]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1.0.0.0", "1.0")]
    [InlineData("v1.0", "1.0")]
    [InlineData("1.9", "1.09")]
    [InlineData("1.2.3", "01.02.03")]
    public void A_change_of_notation_with_no_change_of_number_is_undecidable(string left, string right)
    {
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare(left, right));
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare(right, left));
    }

    [Theory]
    [InlineData("2024.03", "1.4")]
    [InlineData("20240301", "1.4")]
    [InlineData("2024.03.01", "1.4.0")]
    [InlineData("9.1", "100.1")]
    public void A_leading_segment_of_a_different_magnitude_is_a_different_scheme(string left, string right)
    {
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare(left, right));
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare(right, left));
    }

    [Theory]
    [InlineData("2024.03", "2024.11")]
    [InlineData("2024.12", "2025.01")]
    [InlineData("20240301", "20240415")]
    public void Two_versions_in_the_same_date_like_scheme_compare_normally(string earlier, string later)
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(earlier, later));
    }

    [Theory]
    [InlineData("latest", "1.0")]
    [InlineData("", "1.0")]
    [InlineData("final release", "1.0")]
    [InlineData("v", "1.0")]
    [InlineData("hotfix-2", "1.0")]
    public void Free_text_that_is_not_a_version_number_is_undecidable(string left, string right)
    {
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare(left, right));
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare(right, left));
    }

    [Fact]
    public void A_suffix_nobody_can_read_does_not_stop_the_numbers_deciding()
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare("1.0-rc.1", "1.1"));
        Assert.Equal(ModVersionComparison.Later, _comparer.Compare("2.0 (hotfix)", "1.9"));
    }

    [Fact]
    public void A_suffix_nobody_can_read_is_undecidable_against_the_same_numbers()
    {
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare("1.0-rc.1", "1.0"));
    }

    [Fact]
    public void A_segment_too_large_to_be_a_number_is_undecidable()
    {
        Assert.Equal(ModVersionComparison.Undecidable, _comparer.Compare("1.99999999999999999999", "1.2"));
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Assert.Equal(ModVersionComparison.Earlier, _comparer.Compare(" 1.2 ", "1.3"));
    }


    [Theory]
    [InlineData("1.2", "1.3")]
    [InlineData("1.10", "1.9")]
    [InlineData("v1", "1.0")]
    [InlineData("2024.03", "1.4")]
    [InlineData("1.0-beta", "1.0")]
    [InlineData("latest", "1.0")]
    public void Comparing_the_other_way_round_gives_the_mirror_result(string left, string right)
    {
        var forwards = _comparer.Compare(left, right);
        var backwards = _comparer.Compare(right, left);

        Assert.Equal(Mirror(forwards), backwards);
    }


    private static ModVersionComparison Mirror(ModVersionComparison comparison) => comparison switch
    {
        ModVersionComparison.Earlier => ModVersionComparison.Later,
        ModVersionComparison.Later => ModVersionComparison.Earlier,
        _ => comparison
    };
}
