using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Imagery;

/// <summary>
/// Derives a version's imagery from the archive, uploads whatever the server does not already hold,
/// and points the version at it.
/// </summary>
/// <remarks>
/// Called after a successful registration, and never before one. The mod file is verified before
/// metadata is written; imagery gets the opposite treatment, because an import of 2,000 mods must
/// not fail - or worse, half-fail - over a thumbnail upload that timed out. A version with no
/// images renders with initials, exactly as a local mod without an icon does today, until a client
/// that holds the file notices and closes the gap.
/// </remarks>
public interface IModImagePublisher
{
    Task PublishAsync(Guid repoId, ModKey modId, ModVersionKey versionId, LocalMod mod, CancellationToken cancellationToken);
}


/// <summary>
/// The same work, for the caller that needs what came out of it: a client about to render a
/// registered version that has no imagery, while holding the mod file, is exactly the client that
/// should generate the missing derivatives - and it can draw them the moment it has.
/// </summary>
/// <remarks>
/// This is what makes backfill opportunistic rather than a sweep. The gap is closed by the first
/// person who looks at the mod while holding it, which is the most likely thing to happen anyway.
/// </remarks>
public interface IModImageBackfill
{
    /// <summary>
    /// What the version now points at, or an empty set if nothing could be derived or uploaded.
    /// Never throws: imagery is decoration.
    /// </summary>
    Task<IReadOnlyList<ModImageReference>> BackfillAsync(Guid repoId, ModKey modId, ModVersionKey versionId, LocalMod mod, CancellationToken cancellationToken);
}
