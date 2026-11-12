using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Tests.GameAdapters;

public class ModHubModSourceTests
{
    private static readonly DateTime _older = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _newer = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);


    [Fact]
    public async Task A_lookup_in_batches_is_as_current_as_its_stalest_batch()
    {
        var source = new ModHubModSource(new FakeModHubClient(_newer, _older), "fs2025");

        var lookup = await source.LookUpAsync(Mods(2500), CancellationToken.None);

        Assert.Equal(new DateTimeOffset(_older), lookup.CurrentAsOf);
    }

    [Fact]
    public async Task A_lookup_with_any_batch_of_unknown_age_is_of_unknown_age()
    {
        var source = new ModHubModSource(new FakeModHubClient(null, _newer), "fs2025");

        var lookup = await source.LookUpAsync(Mods(2500), CancellationToken.None);

        Assert.Null(lookup.CurrentAsOf);
    }


    private static List<ModKey> Mods(int count) => [.. Enumerable.Range(0, count).Select(x => ModKey.From($"FS25_Mod{x:D5}"))];


    private sealed class FakeModHubClient(params DateTime?[] asOfPerBatch) : IModHubClient
    {
        private int _calls;

        public Task<LookUpModHubModsResponse> LookUpModHubModsV1Async(string game, LookUpModHubModsRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new LookUpModHubModsResponse { CurrentAsOf = asOfPerBatch[_calls++], Mods = [] });
    }
}
