using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// One row of a profile's mod list, resolved far enough to be read rather than edited: the version
/// this profile holds the mod at, and whether that pin can move.
/// </summary>
/// <remarks>
/// The editor works in <see cref="ProfileModPin"/>, which is keys alone, because it has the whole
/// catalog loaded to resolve them against. This is the shape for somebody who only wants to know
/// what is in the profile - a guest, who cannot edit it and should not pay for a disk scan to look.
/// It still carries a whole <see cref="CatalogModVersion"/> so the reader gets the same list row as
/// everywhere else: the icon, the description and the details dialog all come off that record, and
/// a registered version answers every one of them without a scan.
/// There is no unresolved form of this. A <c>ModDependency</c> cannot name a version the repo does
/// not hold - the foreign key onto <c>ModVersions</c> is required and <c>Restrict</c>, so the
/// version cannot be deleted out from under it - so every pin resolves to a registered record.
/// </remarks>
public sealed record PinnedMod(CatalogModVersion Version, ProfileModLock Lock)
{
    public ModKey ModId => Version.ModId;
    public ModVersionKey VersionId => Version.VersionId;
    public string DisplayName => Version.Name;
}
