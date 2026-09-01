using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// One row of a profile's mod list, resolved far enough to be read rather than edited: the name the
/// mod calls itself, the version this profile holds it at, and whether that pin can move.
/// </summary>
/// <remarks>
/// The editor works in <see cref="ProfileModPin"/>, which is keys alone, because it has the whole
/// catalog loaded to resolve them against. This is the shape for somebody who only wants to know
/// what is in the profile - a guest, who cannot edit it and should not pay for a disk scan to look.
/// </remarks>
public sealed record PinnedMod(
    ModKey ModId,
    ModVersionKey VersionId,
    string DisplayName,
    ProfileModLock Lock,
    bool IsRegistered)
{
    /// <summary>
    /// The pinned version is not in the repo's mod list. A profile can outlive the version it pins -
    /// a member can delete one - and a reader is better told that than shown a blank name.
    /// </summary>
    public static PinnedMod Unresolved(ModKey modId, ModVersionKey versionId, ProfileModLock modLock)
        => new(modId, versionId, modId.Value, modLock, false);
}
