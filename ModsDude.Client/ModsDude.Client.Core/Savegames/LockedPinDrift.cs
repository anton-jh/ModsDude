using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Savegames;

/// <remarks>
/// Answers are cached for the session: a revision never changes once written. A failed read is not
/// cached, because a dropped connection is not an answer about two revisions.
/// </remarks>
public sealed class LockedPinDrift(IProfileRevisionComparer comparer, ILogger<LockedPinDrift> logger) : ILockedPinDrift
{
    private readonly Dictionary<(Guid ProfileId, int From, int To), bool> _answers = [];


    public async Task<bool> HasMovedAsync(Guid repoId, Guid profileId, int from, int to, CancellationToken cancellationToken)
    {
        if (_answers.TryGetValue((profileId, from, to), out var cached))
        {
            return cached;
        }

        try
        {
            var comparison = await comparer.CompareRevisions(repoId, profileId, from, to, cancellationToken);

            var moved = comparison.Changes.Any(x => x.VersionMoved && (x.FromLocked || x.ToLocked || x.Version.Locked));

            _answers[(profileId, from, to)] = moved;

            return moved;
        }
        catch (ApiException exception)
        {
            // Only a chip's colour depends on this, and the quiet answer is the one to guess.
            logger.LogWarning(
                exception,
                "Could not compare revisions {From} and {To} of profile {ProfileId}; reporting no locked pin moved.",
                from, to, profileId);

            return false;
        }
    }
}
