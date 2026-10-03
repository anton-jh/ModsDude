using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Savegames;

public interface ISavegameCompatibilityCheck
{
    /// <summary>
    /// How far the profile's latest revision has moved from the one a savegame was last played on.
    /// Null where it has not moved.
    /// </summary>
    /// <exception cref="ModsDudeServer.Generated.ApiException">The revisions could not be read.</exception>
    Task<SavegameCompatibilityVerdict?> AssessAsync(
        Guid repoId,
        Guid profileId,
        int playedRevision,
        int latestRevision,
        SavegameCompatibilityPolicy policy,
        CancellationToken cancellationToken);
}
