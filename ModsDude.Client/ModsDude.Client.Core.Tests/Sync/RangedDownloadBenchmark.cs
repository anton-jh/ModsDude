using ModsDude.Client.Core.Sync;
using System.Diagnostics;
using Xunit.Abstractions;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// Measures real throughput against real blob storage, for tuning <see cref="RangedDownloadOptions"/>.
/// </summary>
/// <remarks>
/// Does nothing - and passes - unless <c>MODSDUDE_BENCH_SAS</c> holds a read SAS link to a large
/// blob (the Download link a sync mints works; it lives 30 minutes). Run it on its own and read the
/// output:
/// <code>
/// $env:MODSDUDE_BENCH_SAS = '...'
/// dotnet test --filter RangedDownloadBenchmark --logger "console;verbosity=detailed"
/// </code>
/// </remarks>
public class RangedDownloadBenchmark(ITestOutputHelper output)
{
    [Fact]
    public async Task Throughput_by_connections_and_chunk_size()
    {
        var link = Environment.GetEnvironmentVariable("MODSDUDE_BENCH_SAS");

        if (string.IsNullOrWhiteSpace(link))
        {
            output.WriteLine("MODSDUDE_BENCH_SAS is not set; nothing measured.");

            return;
        }

        using var httpClient = new HttpClient();

        // A single plain stream first, as the baseline everything else is compared against.
        await MeasureAsync(httpClient, link, "single GET", new RangedDownloadOptions { ChunkSize = int.MaxValue });

        foreach (var chunkMegabytes in new[] { 4, 8, 16 })
        {
            foreach (var connections in new[] { 2, 4, 8, 12, 16 })
            {
                var options = new RangedDownloadOptions
                {
                    ChunkSize = chunkMegabytes * 1024 * 1024,
                    ConnectionsPerFile = connections,
                    WindowChunks = connections + 4,
                    ConnectionBudget = new SemaphoreSlim(connections)
                };

                await MeasureAsync(httpClient, link, $"{chunkMegabytes,2} MB x {connections,2}", options);
            }
        }
    }

    private async Task MeasureAsync(HttpClient httpClient, string link, string label, RangedDownloadOptions options)
    {
        var downloader = new HttpModFileDownloader(httpClient, options);
        var stopwatch = Stopwatch.StartNew();

        using var download = await downloader.OpenAsync(link, null, CancellationToken.None);

        var buffer = new byte[1024 * 1024];
        long total = 0;
        int read;

        while ((read = await download.Content.ReadAsync(buffer)) > 0)
        {
            total += read;
        }

        stopwatch.Stop();

        var megabits = total * 8 / 1_000_000d / stopwatch.Elapsed.TotalSeconds;

        output.WriteLine($"{label,-12} {total / 1024 / 1024,6} MB in {stopwatch.Elapsed.TotalSeconds,6:0.0} s = {megabits,6:0} Mb/s");
    }
}
