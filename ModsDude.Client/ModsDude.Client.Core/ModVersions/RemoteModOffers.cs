using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.ModVersions;

/// <summary>
/// Which versions a remote source has that are worth pointing somebody at: the ones newer than
/// everything already here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Newer than every version known, not than the one pinned.</b> A version already in a folder or in
/// the repo is one the editor can offer by itself; sending somebody to a website to download it again
/// would be noise. That is also what makes an offer go away once its file is downloaded and scanned -
/// the version becomes known, and a known version is never offered.
/// </para>
/// <para>
/// <b>Abstentions do not count as newer.</b> A version the comparer cannot place against something
/// already here is left out, as it is for updates: offering a possible downgrade as a newer version is
/// worse than saying nothing. It has to be placed after at least one known version, and before or
/// level with none.
/// </para>
/// </remarks>
public static class RemoteModOffers
{
    public static IReadOnlyDictionary<ModKey, RemoteModOffer> Newer(
        IEnumerable<RemoteModOffer> offers,
        IReadOnlyDictionary<ModKey, ModVersionSet> known,
        IModVersionComparer comparer)
    {
        var result = new Dictionary<ModKey, RemoteModOffer>();

        foreach (var offer in offers)
        {
            if (known.TryGetValue(offer.ModId, out var set) is false || set.Find(offer.VersionId) is not null)
            {
                continue;
            }

            if (IsNewerThanAll(offer.VersionId, set, comparer))
            {
                // A source listing two files that normalise to one mod id is its own oddity; the first
                // answer stands rather than one silently replacing the other.
                result.TryAdd(offer.ModId, offer);
            }
        }

        return result;
    }


    private static bool IsNewerThanAll(ModVersionKey candidate, ModVersionSet set, IModVersionComparer comparer)
    {
        var placedAfterAny = false;

        foreach (var version in set.Order)
        {
            switch (comparer.Compare(candidate, version.VersionId))
            {
                case ModVersionComparison.Later:
                    placedAfterAny = true;
                    break;

                case ModVersionComparison.Undecidable:
                    break;

                default:
                    return false;
            }
        }

        return placedAfterAny;
    }
}
