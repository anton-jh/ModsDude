using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

/// <summary>
/// ModHub, as the ModsDude server has read it. Lookups go to the server's copy; only a download the
/// user picked goes to ModHub's CDN.
/// </summary>
/// <remarks>
/// A ModHub archive is named after the mod, which is what <see cref="ModKey"/> is built from, so a key
/// is what is sent and the server matches it on the file name ignoring case and extension.
/// </remarks>
public sealed class ModHubModSource(IModHubClient client, string game) : IRemoteModSource
{
    /// <summary>Asked in batches, well inside what the server accepts in one request.</summary>
    private const int _batchSize = 2000;

    private const string _downloadHostSuffix = ".giants-software.com";


    public string Key => "modhub";
    public string DisplayName => "ModHub";

    /// <summary>The CDN answers 403 to a request whose Referer is not ModHub's site.</summary>
    public Uri DownloadReferrer { get; } = new("https://www.farming-simulator.com/");


    public async Task<RemoteModLookup> LookUpAsync(IReadOnlyCollection<ModKey> mods, CancellationToken cancellationToken)
    {
        var versions = new List<RemoteModVersion>();
        DateTimeOffset? currentAsOf = null;

        foreach (var batch in mods.Select(x => x.Value).Order(StringComparer.Ordinal).Chunk(_batchSize))
        {
            var response = await client.LookUpModHubModsV1Async(
                game, new LookUpModHubModsRequest { Names = batch }, cancellationToken);

            // The generated client hands dates back as local or UTC depending on the payload's offset;
            // the DateTimeOffset constructor honours either kind.
            currentAsOf = response.CurrentAsOf is DateTime asOf ? new DateTimeOffset(asOf) : null;

            foreach (var mod in response.Mods)
            {
                if (string.IsNullOrWhiteSpace(mod.Version)
                    || Uri.TryCreate(mod.DownloadUrl, UriKind.Absolute, out var download) is false
                    || IsAllowedDownload(download) is false)
                {
                    continue;
                }

                versions.Add(new RemoteModVersion(
                    ModKey.From(mod.Name),
                    ModVersionKey.From(mod.Version),
                    mod.Title,
                    mod.Author,
                    mod.FileName,
                    download));
            }
        }

        return new RemoteModLookup(currentAsOf, versions);
    }

    public bool IsAllowedDownload(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.EndsWith(_downloadHostSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>ModHub's code for the game a repo is about, or null where the server does not crawl it.</summary>
    public static string? GameFor(FarmingSimulatorGameVersion? version) => version switch
    {
        FarmingSimulatorGameVersion.Fs25 => "fs2025",
        _ => null
    };
}

internal sealed class FarmingSimulatorRemoteModSourcesAdapter(IReadOnlyList<IRemoteModSource> sources) : IRemoteModSourcesAdapter
{
    public IReadOnlyList<IRemoteModSource> Sources { get; } = sources;
}
