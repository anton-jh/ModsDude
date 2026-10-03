using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Persistence.Retention;

public interface IRetentionUpkeep
{
    /// <summary>After a savegame gained or lost a snapshot.</summary>
    Task ReleaseSavegameAsync(RepoId repoId, SavegameId savegameId, CancellationToken cancellationToken);

    /// <summary>After a profile gained or lost a revision, or a snapshot was played on one of them.</summary>
    Task ReleaseProfileAsync(RepoId repoId, ProfileId profileId, CancellationToken cancellationToken);

    /// <summary>After mods gained, lost or reordered a version, or started being pinned.</summary>
    Task ReleaseModsAsync(RepoId repoId, IReadOnlyCollection<ModId> modIds, CancellationToken cancellationToken);

    /// <summary>After a revision was saved: the mods it pins that have a version scheduled.</summary>
    Task ReleaseModsPinnedByAsync(RepoId repoId, ProfileId profileId, RevisionNumber revision, CancellationToken cancellationToken);
}
