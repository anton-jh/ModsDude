using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Services;
using System.Collections.Concurrent;

namespace ModsDude.Client.Core.Savegames;

/// <remarks>
/// Comparisons are cached for the session: a revision never changes once written. A failed read is
/// not cached.
/// </remarks>
public sealed class SavegameCompatibilityCheck(IProfileRevisionComparer comparer) : ISavegameCompatibilityCheck
{
    private readonly ConcurrentDictionary<(Guid ProfileId, int From, int To), ProfileRevisionComparison> _comparisons = [];


    public async Task<SavegameCompatibilityVerdict?> AssessAsync(
        Guid repoId,
        Guid profileId,
        int playedRevision,
        int latestRevision,
        SavegameCompatibilityPolicy policy,
        CancellationToken cancellationToken)
    {
        if (latestRevision <= playedRevision)
        {
            return null;
        }

        var key = (profileId, playedRevision, latestRevision);

        if (_comparisons.TryGetValue(key, out var comparison) is false)
        {
            comparison = await comparer.CompareRevisions(repoId, profileId, playedRevision, latestRevision, cancellationToken);

            _comparisons[key] = comparison;
        }

        return SavegameCompatibility.Assess(comparison, policy);
    }
}
