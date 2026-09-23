using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

/// <summary>
/// ModHub, as the ModsDude server has read it. Nothing here reaches ModHub itself: the server crawls it
/// and answers from what it stored, so every member asking costs ModHub nothing.
/// </summary>
/// <remarks>
/// A ModHub archive is named after the mod, which is exactly what <see cref="ModKey"/> is built from,
/// so a key is what is sent and the server matches it on the file name ignoring case and extension.
/// </remarks>
public sealed class FarmingSimulatorModHubSource(IModHubClient client, string game) : IRemoteModSource
{
    /// <summary>
    /// Asked in batches, well inside what the server accepts in one request.
    /// </summary>
    private const int _batchSize = 2000;


    public string Key => "modhub";
    public string DisplayName => "ModHub";


    public async Task<RemoteModLookup> LookUpAsync(IReadOnlyCollection<ModKey> mods, CancellationToken cancellationToken)
    {
        var offers = new List<RemoteModOffer>();
        DateTimeOffset? currentAsOf = null;

        foreach (var batch in mods.Select(x => x.Value).Chunk(_batchSize))
        {
            var response = await client.LookUpModHubModsV1Async(
                game, new LookUpModHubModsRequest { Names = batch }, cancellationToken);

            // The generated client hands dates back as local or UTC depending on the payload's offset;
            // the DateTimeOffset constructor honours either kind.
            currentAsOf = response.CurrentAsOf is DateTime asOf ? new DateTimeOffset(asOf) : null;

            foreach (var mod in response.Mods)
            {
                if (string.IsNullOrWhiteSpace(mod.Version))
                {
                    continue;
                }

                offers.Add(new RemoteModOffer(
                    ModKey.From(mod.Name),
                    ModVersionKey.From(mod.Version),
                    mod.Title,
                    mod.PageUrl,
                    mod.DownloadUrl));
            }
        }

        return new RemoteModLookup(currentAsOf, offers);
    }

    /// <summary>
    /// ModHub's code for the game a repo is about, or null where the server does not crawl it. Only
    /// Farming Simulator 25 is.
    /// </summary>
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
