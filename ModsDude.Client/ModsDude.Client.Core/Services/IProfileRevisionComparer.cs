using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Services;

public interface IProfileRevisionComparer
{
    /// <summary>
    /// What changed between two revisions of a profile, mod by mod.
    /// </summary>
    /// <remarks>
    /// Two dependency reads and one walk of the registered mod list, rather than two calls to
    /// <see cref="IProfileService.GetPinnedMods"/>, each of which would walk it again.
    /// </remarks>
    Task<ProfileRevisionComparison> CompareRevisions(
        Guid repoId, Guid profileId, int from, int to, CancellationToken cancellationToken);
}
