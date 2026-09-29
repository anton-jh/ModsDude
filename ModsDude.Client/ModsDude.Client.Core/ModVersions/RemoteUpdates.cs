using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.ModVersions;

/// <summary>
/// Which versions a remote update provider has that are worth pointing somebody at: the ones newer than
/// everything already here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Newer than every version known, not than the one pinned.</b> A version already in a folder or in
/// the repo is one the editor can offer by itself; sending somebody to a website to download it again
/// would be noise. That is also what makes an update go away once its file is downloaded and scanned -
/// the version becomes known, and a known version is never an update.
/// </para>
/// <para>
/// <b>Abstentions do not count as newer.</b> A version the comparer cannot place against something
/// already here is left out, as it is for updates: offering a possible downgrade as a newer version is
/// worse than saying nothing. It has to be placed after at least one known version, and before or
/// level with none.
/// </para>
/// </remarks>
public static class RemoteUpdates
{
    public static IReadOnlyDictionary<ModKey, RemoteModVersion> Newer(
        IEnumerable<RemoteModVersion> versions,
        IReadOnlyDictionary<ModKey, ModVersionSet> known,
        IModVersionComparer comparer)
    {
        var result = new Dictionary<ModKey, RemoteModVersion>();

        foreach (var version in versions)
        {
            if (known.TryGetValue(version.ModId, out var set) is false || set.Find(version.VersionId) is not null)
            {
                continue;
            }

            if (IsNewerThanAll(version.VersionId, set, comparer))
            {
                // A provider listing two files that normalise to one mod id is its own oddity; the first
                // answer stands rather than one silently replacing the other.
                result.TryAdd(version.ModId, version);
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
