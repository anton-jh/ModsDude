using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;

namespace ModsDude.Client.Core.RemoteSources;

/// <summary>
/// Which versions the remote sources offer, placed into the version order of the mods already known.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only versions newer than everything known.</b> A version already here is offered by its own
/// source, and an older one would be a downgrade. A version the comparer cannot place against some
/// known version is still offered when it is placed after at least one and before none; the pairs it
/// could not place are recorded as unordered, exactly as a derived order records them.
/// </para>
/// <para>
/// <b>Appended rather than derived.</b> An offer is after every version it was compared with, so it
/// goes at the end of its mod's order and nothing already ordered moves.
/// </para>
/// <para>
/// <b>A link whose earlier download held something else is not offered.</b> It is still known, so a pin
/// made on it resolves and can be shown as broken.
/// </para>
/// </remarks>
public static class RemoteModOffers
{
    public static RemoteOfferPlacement Place(
        IReadOnlyDictionary<ModKey, ModVersionSet> index,
        IReadOnlyList<RemoteSourceAnswers> answers,
        IModVersionComparer comparer,
        Func<Uri, RemoteDownloadNote?> notes)
    {
        var placed = new Dictionary<ModKey, ModVersionSet>();
        var visible = new List<CatalogModVersion>();
        var known = new Dictionary<ModVersionIdentity, CatalogModVersion>();

        foreach (var answer in answers)
        {
            var current = answer.Current
                .OrderBy(x => x.ModId.Value, StringComparer.Ordinal)
                .ThenBy(x => x.VersionId.Value, StringComparer.Ordinal);

            foreach (var version in current)
            {
                if (index.TryGetValue(version.ModId, out var set) is false || set.Find(version.VersionId) is not null)
                {
                    continue;
                }

                var offer = CatalogModVersion.FromRemote(new RemoteOffer(answer.Source, version, notes(version.DownloadUrl)));

                known.TryAdd(offer.Identity, offer);

                // The first source to offer a newer version of a mod wins; a second would be a rival
                // newest the order cannot tell apart from the first.
                if (offer.Download!.IsBroken
                    || placed.ContainsKey(version.ModId)
                    || Place(offer, set, comparer) is not ModVersionSet extended)
                {
                    continue;
                }

                placed[version.ModId] = extended;

                if (answer.IsEnabled)
                {
                    visible.Add(offer);
                }
            }

            foreach (var version in answer.Retained)
            {
                known.TryAdd(
                    version.Identity,
                    CatalogModVersion.FromRemote(new RemoteOffer(answer.Source, version, notes(version.DownloadUrl))));
            }
        }

        var extendedIndex = new Dictionary<ModKey, ModVersionSet>(index);

        foreach (var (modId, set) in placed)
        {
            extendedIndex[modId] = set;
        }

        return new RemoteOfferPlacement(extendedIndex, visible, [.. known.Values]);
    }


    /// <summary>The mod's order with the offer at its end, or null where the offer is not newer than everything.</summary>
    private static ModVersionSet? Place(CatalogModVersion offer, ModVersionSet set, IModVersionComparer comparer)
    {
        var placedAfterAny = false;
        var unordered = new List<ModVersionPair>();

        foreach (var version in set.Order)
        {
            switch (comparer.Compare(offer.VersionId, version.VersionId))
            {
                case ModVersionComparison.Later:
                    placedAfterAny = true;
                    break;

                case ModVersionComparison.Undecidable:
                    unordered.Add(new ModVersionPair(version.VersionId, offer.VersionId));
                    break;

                default:
                    return null;
            }
        }

        return placedAfterAny
            ? new ModVersionSet([.. set.Order, offer], [.. set.Unordered, .. unordered])
            : null;
    }
}


/// <param name="Index">The known order, with every placed offer at the end of its mod.</param>
/// <param name="Visible">The placed offers of enabled sources: what selectors and updates may offer.</param>
/// <param name="Known">Every offer a pin may name, placed or not.</param>
public sealed record RemoteOfferPlacement(
    IReadOnlyDictionary<ModKey, ModVersionSet> Index,
    IReadOnlyList<CatalogModVersion> Visible,
    IReadOnlyList<CatalogModVersion> Known);
