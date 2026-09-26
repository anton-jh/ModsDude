using ModsDude.Server.Application.Dependencies;

namespace ModsDude.Server.ModHub.Tests;

/// <summary>
/// Held against pages saved from the live site, trimmed of the header, footer and category menu.
/// When ModHub changes its markup these are the tests to refresh, from freshly saved pages.
/// </summary>
public class ModHubParserTests
{
    [Fact]
    public void A_listing_page_yields_the_grid_in_order()
    {
        var ids = ModHubParser.ParseLatestPage(Fixture("latest-page-0.html")).ModIds;

        Assert.Equal(24, ids.Count);
        Assert.Equal([371282, 370866, 372928], ids.Take(3));
        Assert.Equal(303303, ids[^1]);
    }

    [Fact]
    public void The_featured_mods_above_the_grid_are_not_part_of_the_order()
    {
        var ids = ModHubParser.ParseLatestPage(Fixture("latest-page-0.html")).ModIds;

        Assert.DoesNotContain(302491, ids);
        Assert.DoesNotContain(325826, ids);
        Assert.DoesNotContain(305991, ids);
    }

    [Fact]
    public void A_page_past_the_end_of_the_listing_is_empty()
    {
        var page = ModHubParser.ParseLatestPage(Fixture("latest-past-end.html"));

        Assert.Empty(page.ModIds);
        Assert.True(page.IsPastEnd);
    }

    [Fact]
    public void The_paid_DLCs_mixed_into_the_grid_are_left_out()
    {
        var page = ModHubParser.ParseLatestPage(Fixture("latest-with-dlcs.html"));

        Assert.Equal(17, page.ModIds.Count);
        Assert.Equal([368361, 318936, 368502], page.ModIds.Take(3));
        Assert.Equal(364237, page.ModIds[^1]);
        Assert.False(page.IsPastEnd);
    }

    [Fact]
    public void A_page_of_nothing_but_DLCs_is_not_past_the_end()
    {
        var page = ModHubParser.ParseLatestPage(Fixture("latest-with-dlcs.html").Replace("mod.php?mod_id=", "dlc-detail.php?dlc_id="));

        Assert.Empty(page.ModIds);
        Assert.False(page.IsPastEnd);
    }

    [Fact]
    public void A_listed_item_linking_to_neither_a_mod_nor_a_DLC_is_unreadable()
    {
        var html = Fixture("latest-with-dlcs.html").Replace("dlc-detail.php?dlc_id=fs25vredo", "bundle.php?id=fs25vredo");

        Assert.Throws<ModHubUnreadableException>(() => ModHubParser.ParseLatestPage(html));
    }

    [Fact]
    public void A_page_that_is_not_a_listing_is_unreadable_rather_than_empty()
    {
        Assert.Throws<ModHubUnreadableException>(() => ModHubParser.ParseLatestPage("<html><body><p>Down for maintenance</p></body></html>"));
    }

    [Fact]
    public void A_mod_page_yields_its_details()
    {
        var mod = ModHubParser.ParseModPage(Fixture("mod-found.html"));

        Assert.NotNull(mod);
        Assert.Equal("FS25_autoTurnOffTurnLights.zip", mod.FileName);
        Assert.Equal("Auto Turn Off For Turn Lights", mod.Title);
        Assert.Equal("Ifko[nator]", mod.Author);
        Assert.Equal("1.0.1.2", mod.Version);
        Assert.Equal(new DateOnly(2026, 6, 25), mod.Released);
        Assert.Equal("78 KB", mod.Size);
        Assert.Equal("https://cdn27.giants-software.com/modHub/storage/00303378/FS25_autoTurnOffTurnLights.zip", mod.DownloadUrl);
    }

    [Fact]
    public void ModHubs_not_found_page_is_no_mod()
    {
        Assert.Null(ModHubParser.ParseModPage(Fixture("mod-not-found.html")));
    }

    [Fact]
    public void A_mod_page_without_details_or_an_error_is_unreadable()
    {
        Assert.Throws<ModHubUnreadableException>(() => ModHubParser.ParseModPage("<html><body><h2 class=\"title-label\">Something</h2></body></html>"));
    }

    [Fact]
    public void A_mod_page_without_a_version_is_unreadable()
    {
        var html = Fixture("mod-found.html").Replace("<b>Version</b>", "<b>Revision</b>");

        Assert.Throws<ModHubUnreadableException>(() => ModHubParser.ParseModPage(html));
    }


    private static string Fixture(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    }
}
