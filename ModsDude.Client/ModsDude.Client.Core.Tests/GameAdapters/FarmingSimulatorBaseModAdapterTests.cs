using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.Models;
using System.IO.Compression;
using System.Text;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// The adapter is exercised through a real folder of real archives, because everything interesting
/// about it - what it accepts, what it skips, and what it reads out of modDesc - is decided by the
/// zip's contents rather than by anything a fake could stand in for.
/// </summary>
public class FarmingSimulatorBaseModAdapterTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("modsdude-adapter-tests").FullName;


    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Directory.Delete(_folder, true);
    }


    [Fact]
    public async Task A_mod_declaring_a_map_is_locked()
    {
        WriteMod("FS25_BigMap", "1.0.0.0", maps: ["mapUS"]);

        var mod = await ScanOne();

        Assert.True(mod.Locked);
    }

    [Fact]
    public async Task A_mod_declaring_no_maps_is_not_locked()
    {
        WriteMod("FS25_ScriptThing", "1.0.0.0", maps: []);

        var mod = await ScanOne();

        Assert.False(mod.Locked);
    }

    /// <summary>
    /// An empty <c>maps</c> element is what a mod that once shipped a map and no longer does leaves
    /// behind. Nothing is version-sensitive about it, so it must not read as locked.
    /// </summary>
    [Fact]
    public async Task An_empty_maps_element_is_not_a_map_mod()
    {
        WriteMod("FS25_Leftover", "1.0.0.0", maps: [], writeEmptyMapsElement: true);

        var mod = await ScanOne();

        Assert.False(mod.Locked);
    }

    [Fact]
    public async Task Every_version_of_a_map_mod_derives_the_same_answer()
    {
        WriteMod("FS25_BigMap", "1.0.0.0", maps: ["mapUS"]);
        WriteMod("FS25_BigMap_v2", "2.0.0.0", maps: ["mapUS"]);

        var mods = await Scan();

        Assert.All(mods, x => Assert.True(x.Locked));
    }


    private async Task<LocalMod> ScanOne()
    {
        return Assert.Single(await Scan());
    }

    private async Task<IReadOnlyList<LocalMod>> Scan()
    {
        var mods = await new FarmingSimulatorBaseModAdapter().GetModsFromFolder(_folder, CancellationToken.None);

        return [.. mods];
    }

    private void WriteMod(string id, string version, string[] maps, bool writeEmptyMapsElement = false)
    {
        var mapsElement = maps.Length > 0 || writeEmptyMapsElement
            ? $"<maps>{string.Concat(maps.Select(x => $"<map id=\"{x}\" className=\"{x}\" filename=\"maps/{x}.xml\" />"))}</maps>"
            : string.Empty;

        var modDesc = $"""
            <?xml version="1.0" encoding="utf-8" standalone="no"?>
            <modDesc descVersion="95">
                <author>Someone</author>
                <version>{version}</version>
                <title><en>{id}</en></title>
                <description><en>A mod.</en></description>
                {mapsElement}
            </modDesc>
            """;

        using var file = File.Create(Path.Combine(_folder, $"{id}.zip"));
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        using var entry = archive.CreateEntry("modDesc.xml").Open();

        entry.Write(Encoding.UTF8.GetBytes(modDesc));
    }
}
