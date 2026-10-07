using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// What a profile holds beyond its place in the list: its history, what it pins, and archived ones.
/// The live list itself is <see cref="IProfileStore"/>.
/// </summary>
public interface IProfileService : IProfileRevisionComparer
{
    /// <summary>
    /// Asks the server for one profile of any repo this account is in, archived ones included, or
    /// null where it does not exist.
    /// </summary>
    Task<ProfileDto?> FindProfile(Guid repoId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// The repo's archived profiles. Read on demand rather than held: the Archive is a page somebody
    /// visits, not a thing the shell is built from.
    /// </summary>
    Task<IReadOnlyList<ProfileDto>> GetArchivedProfiles(Guid repoId, CancellationToken cancellationToken);

    /// <summary>How many mods the profile's current revision pins and how big they are.</summary>
    Task<ProfileModStatistics> GetModStatistics(Guid repoId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// The profile's history, newest first, with the number of the revision that is current.
    /// </summary>
    Task<ProfileHistory> GetHistory(Guid repoId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes old revisions, which is how the mod versions they pin stop being undeletable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The head is refused by the server, so nothing here can change what the profile currently pins
    /// - which is why the store's head revision needs no touching afterwards.
    /// </para>
    /// <para>
    /// One request for the whole selection. It deletes what it can and names what it cannot, so a
    /// hundred revisions blocked by one savegame is an answer rather than an exercise in bisection.
    /// </para>
    /// </remarks>
    Task<PruneProfileRevisionsResponse> PruneRevisions(
        Guid repoId, Guid profileId, IReadOnlyList<int> revisions, CancellationToken cancellationToken);

    /// <summary>
    /// What the profile pins, with each version resolved to the registered record behind it - which
    /// is what the shared list row needs to render one.
    /// </summary>
    /// <remarks>
    /// Two reads and a join, and deliberately not a <c>ModCatalog</c>: the catalog exists to merge
    /// the repo's mods with what is on this machine's disks, and a reader who cannot edit the profile
    /// has no use for the local half and should not pay a scan for it. Both routes are readable at
    /// Guest, which is the level this is for.
    /// </remarks>
    /// <param name="revision">
    /// Which revision to read, or <c>null</c> for the profile's current one. An older revision is
    /// the same list rendered the same way - it is only read-only because nothing anywhere can write
    /// to one.
    /// </param>
    Task<IReadOnlyList<PinnedMod>> GetPinnedMods(Guid repoId, Guid profileId, int? revision, CancellationToken cancellationToken);
}
