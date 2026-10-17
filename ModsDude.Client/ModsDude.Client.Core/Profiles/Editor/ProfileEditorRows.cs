using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>One entry in a row's version selector.</summary>
/// <param name="CouldNotCompare">The ordering could not place this version against the repo's newest.</param>
public sealed record ProfileModVersionOption(CatalogModVersion Version, bool CouldNotCompare = false)
{
    public string Label => Version switch
    {
        { IsOnServer: true } => Version.VersionId.Value,
        { Download: RemoteOffer offer } => $"{Version.VersionId.Value} · {offer.SourceName}",
        _ => $"{Version.VersionId.Value}*"
    };

    public string? Note
    {
        get
        {
            if (Version.IsOnServer)
            {
                return null;
            }

            var action = Version.Download is RemoteOffer offer
                ? $"Downloads from {offer.SourceName} and imports on save."
                : "Imports on save.";

            return CouldNotCompare
                ? $"{action} The order between this and what the repo holds is not settled, so importing will ask which comes first."
                : action;
        }
    }
}


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
/// <param name="Sources">Where the version comes from, where that needs saying.</param>
public sealed record AvailableModRow(
    ModKey ModId,
    CatalogModVersion Version,
    IReadOnlyList<ProfileModVersionOption> Options,
    AvailableModStatus Status,
    bool OrderNotSettled,
    bool IsIgnored,
    bool LockedBySource,
    string? Sources,
    string? SortCaption,
    string? SortTooltip);


/// <summary>A mod in the draft, or in the saved profile and taken out of the draft.</summary>
/// <param name="Pin">What the draft pins, with the version's own adapter lock. Null when taken out.</param>
/// <param name="Saved">What the saved profile pins, or null.</param>
/// <param name="Options">Newest first.</param>
/// <param name="HasUnsettledVersion">
/// The pin is unlocked and a visible version exists that the ordering could not place against the repo's newest.
/// </param>
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
    bool HasUnsettledVersion,
    bool NotInSources,
    DateTimeOffset? Added,
    string? SortCaption,
    string? SortTooltip)
{
    public bool IsTakenOut => Pin is null;

    public bool IsPending => Pin is not null && Version.IsOnServer is false;

    /// <summary>Whether the Updates filter shows the row. A locked pin's newer version is not counted as an update.</summary>
    public bool ShowsUnderUpdates => IsTakenOut is false && Lock.IsLocked is false && (Update is not null || HasUnsettledVersion);

    /// <summary>Pins a download whose file turned out to be something else, so a save will ask what to do instead.</summary>
    public bool IsBrokenDownload => Pin is not null && Version.Download is { IsBroken: true };
}
