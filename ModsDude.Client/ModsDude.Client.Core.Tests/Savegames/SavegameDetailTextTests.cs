using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using System.Globalization;

namespace ModsDude.Client.Core.Tests.Savegames;

/// <summary>
/// A detail is stored in one invariant form and worded by whoever reads it, so the same snapshot reads
/// in each reader's own culture and time zone rather than in the uploader's.
/// </summary>
public class SavegameDetailTextTests
{
    private static readonly CultureInfo _english = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo _swedish = CultureInfo.GetCultureInfo("sv-SE");
    private static readonly TimeZoneInfo _utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo _plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus-two", "plus-two");


    [Fact]
    public void Text_is_shown_as_it_is()
    {
        Assert.Equal("Zielonka", SavegameDetailText.Format(SavegameDetailKind.Text, "Zielonka", _english, _utc));
    }

    [Fact]
    public void A_date_is_worded_in_the_readers_culture()
    {
        Assert.Equal("30/11/2024", SavegameDetailText.Format(SavegameDetailKind.Date, "2024-11-30", _english, _utc));
        Assert.Equal("2024-11-30", SavegameDetailText.Format(SavegameDetailKind.Date, "2024-11-30", _swedish, _utc));
    }

    /// <summary>The same moment, in two readers' zones: an evening saved at 23:00 UTC is the next day two hours east.</summary>
    [Fact]
    public void An_instant_is_put_in_the_readers_time_zone()
    {
        const string written = "2024-11-30T23:00:00.0000000+00:00";

        Assert.Equal("30/11/2024 23:00", SavegameDetailText.Format(SavegameDetailKind.Instant, written, _english, _utc));
        Assert.Equal("01/12/2024 01:00", SavegameDetailText.Format(SavegameDetailKind.Instant, written, _english, _plusTwo));
    }

    [Theory]
    [InlineData("42", "42 min")]
    [InlineData("59", "59 min")]
    [InlineData("2717", "45 h")]
    public void A_duration_reads_in_minutes_under_an_hour_and_in_hours_after(string minutes, string expected)
    {
        Assert.Equal(expected, SavegameDetailText.Format(SavegameDetailKind.Duration, minutes, _english, _utc));
    }

    [Fact]
    public void A_value_not_in_its_kinds_form_is_shown_as_it_is()
    {
        Assert.Equal("yesterday", SavegameDetailText.Format(SavegameDetailKind.Date, "yesterday", _english, _utc));
        Assert.Equal("45 h", SavegameDetailText.Format(SavegameDetailKind.Duration, "45 h", _english, _utc));
    }
}
