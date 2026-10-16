using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// Places outside this machine that offer versions of a game's mods, such as ModHub for Farming
/// Simulator. A base-stage capability. A game with none leaves the capability out.
/// </summary>
public interface IRemoteModSourcesAdapter
{
    IReadOnlyList<IRemoteModSource> Sources { get; }
}

/// <summary>
/// One remote source. It answers which versions it has and where each one is downloaded from; the
/// engine does the downloading.
/// </summary>
public interface IRemoteModSource
{
    /// <summary>Stable, and unique among one adapter's sources.</summary>
    string Key { get; }

    string DisplayName { get; }

    /// <summary>What this source has of <paramref name="mods"/>. Only versions with a download it allows.</summary>
    Task<RemoteModLookup> LookUpAsync(IReadOnlyCollection<ModKey> mods, CancellationToken cancellationToken);

    /// <summary>Whether a download may be fetched from, or redirected to, this address.</summary>
    bool IsAllowedDownload(Uri uri);
}

/// <param name="CurrentAsOf">
/// When the answer was last known to be current, or null where the source cannot vouch for it. An
/// answer with no date may be missing most of what the source has.
/// </param>
public sealed record RemoteModLookup(DateTimeOffset? CurrentAsOf, IReadOnlyList<RemoteModVersion> Versions);

/// <summary>A version of a mod that a remote source has, as the source describes it.</summary>
/// <param name="FileName">What the archive is called on the source, extension included.</param>
public sealed record RemoteModVersion(
    ModKey ModId,
    ModVersionKey VersionId,
    string Title,
    string? Author,
    string FileName,
    Uri DownloadUrl)
{
    public ModVersionIdentity Identity => new(ModId, VersionId);
}
