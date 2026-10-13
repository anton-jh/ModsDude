using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Domain.Profiles;

/// <summary>
/// One mod, pinned by one revision at one version, with the profile's own lock.
/// </summary>
/// <remarks>
/// Belongs to the revision's repo, so it holds IDs only. A revision stores its own copies of the pins
/// it is given, which is what stamps <see cref="Added"/>.
/// </remarks>
public sealed class ProfileModPin
{
    public required ModId ModId { get; init; }
    public required ModVersionId VersionId { get; init; }
    public required bool Locked { get; init; }

    /// <summary>
    /// When this mod arrived in the profile at this version: the instant of the revision that first
    /// pinned it, carried forward for as long as every revision after keeps pinning that same version.
    /// A new version, or a mod taken out and put back, starts again. The lock is not part of it,
    /// because toggling one moves no file.
    /// </summary>
    public DateTime Added { get; private set; }


    internal ProfileModPin CopyAddedAt(DateTime added) => new()
    {
        ModId = ModId,
        VersionId = VersionId,
        Locked = Locked,
        Added = added
    };
}
