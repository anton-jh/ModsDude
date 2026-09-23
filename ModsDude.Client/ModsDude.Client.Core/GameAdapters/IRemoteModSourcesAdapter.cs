using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// Places outside this machine that know about newer versions of a game's mods - for Farming
/// Simulator, ModHub. A base-stage capability: what a remote source is depends on which game the repo
/// is about, not on anything about this machine.
/// </summary>
/// <remarks>
/// <para>
/// <b>A remote source only ever points.</b> It says "this version exists, and here is where to get
/// it"; it never hands over bytes, so nothing it answers can be pinned or imported. The file still
/// arrives the ordinary way - into a folder a scan reads - and from then on it is an ordinary version.
/// </para>
/// <para>
/// A game with none leaves the capability out rather than answering with an empty list, so the editor
/// can tell "this game has no such thing" from "none are configured".
/// </para>
/// </remarks>
public interface IRemoteModSourcesAdapter
{
    IReadOnlyList<IRemoteModSource> Sources { get; }
}

public interface IRemoteModSource
{
    /// <summary>
    /// Stable, and unique among one adapter's sources: it is what the source's chip is identified by.
    /// </summary>
    string Key { get; }

    /// <summary>What the source is called on its chip and wherever an offer from it is shown.</summary>
    string DisplayName { get; }

    /// <summary>What this source has of <paramref name="mods"/>, at whatever version it has.</summary>
    Task<RemoteModLookup> LookUpAsync(IReadOnlyCollection<ModKey> mods, CancellationToken cancellationToken);
}

/// <param name="CurrentAsOf">
/// When what was answered was last known to be current, or null where the source cannot yet vouch for
/// it at all - a server still reading ModHub for the first time. An answer with no date may be missing
/// most of what the source has, and must not be presented as "nothing newer".
/// </param>
public sealed record RemoteModLookup(DateTimeOffset? CurrentAsOf, IReadOnlyList<RemoteModOffer> Offers);

/// <summary>A version of a mod that a remote source has, and where a person goes to get it.</summary>
public sealed record RemoteModOffer(
    ModKey ModId,
    ModVersionKey VersionId,
    string Title,
    string PageUrl,
    string? DownloadUrl)
{
    public ModVersionIdentity Identity => new(ModId, VersionId);
}
