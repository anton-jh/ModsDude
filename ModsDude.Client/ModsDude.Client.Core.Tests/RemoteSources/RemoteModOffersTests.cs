using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.RemoteSources;
using static ModsDude.Client.Core.Tests.Keys;
using static ModsDude.Client.Core.Tests.Profiles.Editor.EditorFixtures;

namespace ModsDude.Client.Core.Tests.RemoteSources;

public class RemoteModOffersTests
{
    private readonly FakeRemoteModSource _source = new();


    [Fact]
    public void A_version_newer_than_everything_known_is_offered_at_the_end_of_its_mods_order()
    {
        var placement = Place(Remote("map", "1.2"), Registered("map", "1.0", 0), Local("map", "1.1"));

        var offer = Assert.Single(placement.Visible);
        Assert.Equal(V("1.2"), offer.VersionId);
        Assert.Same(_source, offer.Download!.Source);
        Assert.Equal(Vs("1.0", "1.1", "1.2"), placement.Index[Mod("map")].Order.Select(x => x.VersionId));
        Assert.True(placement.Index[Mod("map")].IsAfter(V("1.2"), V("1.0")));
    }

    [Fact]
    public void A_version_already_known_is_not_offered()
    {
        Assert.Empty(Place(Remote("map", "1.1"), Registered("map", "1.0", 0), Local("map", "1.1")).Visible);
    }

    [Fact]
    public void A_version_older_than_something_known_is_not_offered()
    {
        Assert.Empty(Place(Remote("map", "1.1"), Registered("map", "1.0", 0), Registered("map", "1.2", 1)).Visible);
    }

    [Fact]
    public void The_same_release_written_differently_is_not_offered()
    {
        Assert.Empty(Place(Remote("map", "1.1.0"), Registered("map", "1.1", 0)).Visible);
    }

    [Fact]
    public void A_version_that_cannot_be_placed_against_anything_is_not_offered()
    {
        Assert.Empty(Place(Remote("map", "2024.03"), Registered("map", "1.0", 0)).Visible);
    }

    [Fact]
    public void An_abstention_against_one_version_is_kept_as_unordered_rather_than_guessed()
    {
        var placement = Place(Remote("map", "1.2"), Registered("map", "1.0", 0), Local("map", "beta"));

        var set = placement.Index[Mod("map")];

        Assert.Single(placement.Visible);
        Assert.True(set.IsAfter(V("1.2"), V("1.0")));
        Assert.False(set.IsAfter(V("1.2"), V("beta")));
    }

    [Fact]
    public void A_mod_nothing_here_knows_is_not_offered()
    {
        var placement = Place(Remote("tractor", "1.0"), Registered("map", "1.0", 0));

        Assert.Empty(placement.Visible);
        Assert.False(placement.Index.ContainsKey(Mod("tractor")));
    }

    [Fact]
    public void A_disabled_source_still_orders_its_offers_but_offers_nothing()
    {
        var placement = Place([Answers(_source, enabled: false, Remote("map", "1.2"))], Registered("map", "1.0", 0));

        Assert.Empty(placement.Visible);
        Assert.Equal(V("1.2"), Assert.Single(placement.Known).VersionId);
        Assert.Equal(Vs("1.0", "1.2"), placement.Index[Mod("map")].Order.Select(x => x.VersionId));
    }

    [Fact]
    public void The_first_source_to_offer_a_newer_version_wins()
    {
        var other = new FakeRemoteModSource("other", "Other");

        var placement = Place(
            [Answers(_source, true, Remote("map", "1.2")), Answers(other, true, Remote("map", "1.3"))],
            Registered("map", "1.0", 0));

        Assert.Equal(V("1.2"), Assert.Single(placement.Visible).VersionId);
        Assert.Equal(Vs("1.0", "1.2"), placement.Index[Mod("map")].Order.Select(x => x.VersionId));
    }

    [Fact]
    public void A_link_whose_download_held_something_else_is_known_but_not_offered()
    {
        var remote = Remote("map", "1.2");
        var note = new RemoteDownloadNote(remote.DownloadUrl, remote.Identity, new ModVersionIdentity(Mod("map"), V("1.1")));

        var placement = RemoteModOffers.Place(
            Index(Registered("map", "1.0", 0)),
            [Answers(_source, true, remote)],
            DefaultModVersionComparer.Instance,
            url => url == remote.DownloadUrl ? note : null);

        Assert.Empty(placement.Visible);

        var known = Assert.Single(placement.Known);
        Assert.True(known.Download!.IsBroken);
        Assert.Equal(Vs("1.0"), placement.Index[Mod("map")].Order.Select(x => x.VersionId));
    }

    [Fact]
    public void A_link_whose_download_held_exactly_what_it_offered_is_offered()
    {
        var remote = Remote("map", "1.2");
        var note = new RemoteDownloadNote(remote.DownloadUrl, remote.Identity, remote.Identity);

        var placement = RemoteModOffers.Place(
            Index(Registered("map", "1.0", 0)),
            [Answers(_source, true, remote)],
            DefaultModVersionComparer.Instance,
            _ => note);

        Assert.False(Assert.Single(placement.Visible).Download!.IsBroken);
    }

    [Fact]
    public void A_version_a_later_answer_replaced_still_resolves()
    {
        var earlier = Remote("map", "1.1");
        var later = Remote("map", "1.2");

        var placement = Place(
            [new RemoteSourceAnswers(_source, true, [later], [earlier, later])],
            Registered("map", "1.0", 0));

        Assert.Equal(V("1.2"), Assert.Single(placement.Visible).VersionId);
        Assert.Contains(placement.Known, x => x.VersionId == V("1.1"));
    }


    private RemoteOfferPlacement Place(RemoteModVersion remote, params CatalogModVersion[] known)
        => Place([Answers(_source, true, remote)], known);

    private static RemoteOfferPlacement Place(IReadOnlyList<RemoteSourceAnswers> answers, params CatalogModVersion[] known)
        => RemoteModOffers.Place(Index(known), answers, DefaultModVersionComparer.Instance, _ => null);

    private static IReadOnlyDictionary<ModKey, ModVersionSet> Index(params CatalogModVersion[] known)
        => ModVersionIndex.Build(known, DefaultModVersionComparer.Instance);

    private static RemoteSourceAnswers Answers(IRemoteModSource source, bool enabled, params RemoteModVersion[] versions)
        => new(source, enabled, versions, versions);

    private static RemoteModVersion Remote(string modId, string version) => FakeRemoteModSource.Version(modId, version);
}
