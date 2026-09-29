using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>One entry in a row's version selector.</summary>
/// <param name="CouldNotCompare">The ordering could not place this version against the repo's newest.</param>
public sealed record ProfileModVersionOption(CatalogModVersion Version, bool CouldNotCompare = false)
{
    public string Label => Version.IsOnServer ? Version.VersionId.Value : $"{Version.VersionId.Value}*";

    public string? Note => Version.IsOnServer
        ? null
        : CouldNotCompare
            ? "Imports on save. The order between this and what the repo holds is not settled, so importing will ask which comes first."
            : "Imports on save.";
}


public sealed record RemoteUpdate(RemoteModVersion Version, string ProviderName);


public enum AvailableModStatus
{
    InRepo,
    New,

    /// <summary>Not in the repo, and newer than anything the repo holds of the mod.</summary>
    NewVersion
}


/// <summary>A mod the profile neither holds nor held when the editor opened.</summary>
/// <param name="Options">Newest first.</param>
/// <param name="LockedBySource">Whether an enabled profile source that offers this version locks it.</param>
/// <param name="Sources">The enabled folders the version was found in, where more than one is enabled.</param>
public sealed record AvailableModRow(
    ModKey ModId,
    CatalogModVersion Version,
    IReadOnlyList<ProfileModVersionOption> Options,
    AvailableModStatus Status,
    bool OrderNotSettled,
    bool IsIgnored,
    bool LockedBySource,
    string? Sources,
    RemoteUpdate? RemoteUpdate,
    string? SortCaption,
    string? SortTooltip);


/// <summary>A mod in the draft, or in the saved profile and taken out of the draft.</summary>
/// <param name="Pin">What the draft pins, with the version's own adapter lock. Null when taken out.</param>
/// <param name="Saved">What the saved profile pins, or null.</param>
/// <param name="Options">Newest first.</param>
/// <param name="HasUnsettledVersion">A visible version exists that the ordering could not place against the repo's newest.</param>
/// <param name="NotInSources">No enabled source offers any version of the mod.</param>
/// <param name="Added">When the mod entered the profile at this version. Null for what only the draft has done.</param>
public sealed record PinnedModRow(
    ModKey ModId,
    CatalogModVersion Version,
    IReadOnlyList<ProfileModVersionOption> Options,
    ProfileModPin? Pin,
    ProfileModPin? Saved,
    ProfileModLock Lock,
    ProfileModTouch Touch,
    string? TouchTooltip,
    ProfileModUpdate? Update,
    RemoteUpdate? RemoteUpdate,
    bool HasUnsettledVersion,
    bool NotInSources,
    DateTimeOffset? Added,
    string? SortCaption,
    string? SortTooltip)
{
    public bool IsTakenOut => Pin is null;

    public bool IsPending => Pin is not null && Version.IsOnServer is false;
}
