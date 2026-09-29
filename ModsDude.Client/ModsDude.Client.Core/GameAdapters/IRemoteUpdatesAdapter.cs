using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// Places outside this machine that know about newer versions of a game's mods - for Farming
/// Simulator, ModHub. A base-stage capability: which places there are depends on the game, not on
/// anything about this machine.
/// </summary>
/// <remarks>
/// <para>
/// <b>A provider only ever points.</b> It says "this version exists, and here is where to get it"; it
/// never hands over bytes, so nothing it answers can be pinned or imported. The file still arrives the
/// ordinary way - into a folder a scan reads - and from then on it is an ordinary version.
/// </para>
/// <para>
/// A game with none leaves the capability out rather than answering with an empty list.
/// </para>
/// </remarks>
public interface IRemoteUpdatesAdapter
{
    IReadOnlyList<IRemoteUpdateProvider> Providers { get; }
}

public interface IRemoteUpdateProvider
{
    /// <summary>Stable, and unique among one adapter's providers.</summary>
    string Key { get; }

    /// <summary>What the provider is called wherever an update from it is shown.</summary>
    string DisplayName { get; }

    /// <summary>What this provider has of <paramref name="mods"/>, at whatever version it has.</summary>
    Task<RemoteUpdateLookup> LookUpAsync(IReadOnlyCollection<ModKey> mods, CancellationToken cancellationToken);
}

/// <param name="CurrentAsOf">
/// When what was answered was last known to be current, or null where the provider cannot yet vouch
/// for it. An answer with no date may be missing most of what the provider has, and must not be
/// presented as "nothing newer".
/// </param>
public sealed record RemoteUpdateLookup(DateTimeOffset? CurrentAsOf, IReadOnlyList<RemoteModVersion> Versions);

/// <summary>A version of a mod that a provider has, and where a person goes to get it.</summary>
public sealed record RemoteModVersion(
    ModKey ModId,
    ModVersionKey VersionId,
    string Title,
    string PageUrl,
    string? DownloadUrl)
{
    public ModVersionIdentity Identity => new(ModId, VersionId);
}
