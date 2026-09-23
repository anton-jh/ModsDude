using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.ModVersions;

public class RemoteModOffersTests
{
    [Fact]
    public void A_version_newer_than_everything_known_is_offered()
    {
        var offers = Newer(Offer("map", "1.2"), Known("map", "1.0", "1.1"));

        Assert.Equal(V("1.2"), Assert.Single(offers.Values).VersionId);
    }

    [Fact]
    public void A_version_already_known_is_not_offered()
    {
        // What makes an offer go away once its file has been downloaded and scanned.
        Assert.Empty(Newer(Offer("map", "1.1"), Known("map", "1.0", "1.1")));
    }

    [Fact]
    public void A_version_older_than_something_known_is_not_offered()
    {
        Assert.Empty(Newer(Offer("map", "1.1"), Known("map", "1.0", "1.2")));
    }

    [Fact]
    public void The_same_release_written_differently_is_not_offered()
    {
        Assert.Empty(Newer(Offer("map", "1.1.0"), Known("map", "1.1")));
    }

    [Fact]
    public void A_version_that_cannot_be_placed_against_anything_is_not_offered()
    {
        Assert.Empty(Newer(Offer("map", "2024.03"), Known("map", "1.0")));
    }

    [Fact]
    public void An_abstention_against_one_version_does_not_stop_an_offer_placed_after_another()
    {
        var offers = Newer(Offer("map", "1.2"), Known("map", "1.0", "beta"));

        Assert.Single(offers);
    }

    [Fact]
    public void A_mod_nothing_here_knows_is_not_offered()
    {
        Assert.Empty(Newer(Offer("tractor", "1.0"), Known("map", "1.0")));
    }


    private static IReadOnlyDictionary<ModKey, RemoteModOffer> Newer(RemoteModOffer offer, CatalogModVersion[] known)
    {
        return RemoteModOffers.Newer(
            [offer],
            ModVersionIndex.Build(known, DefaultModVersionComparer.Instance),
            DefaultModVersionComparer.Instance);
    }

    private static RemoteModOffer Offer(string mod, string version)
        => new(Mod(mod), V(version), mod, $"https://example.test/{mod}", null);

    private static CatalogModVersion[] Known(string mod, params string[] versions)
        => [.. versions.Select(x => new CatalogModVersion(Mod(mod), V(x), mod, "", IsLocal: true, IsOnServer: false, Locked: false))];
}
