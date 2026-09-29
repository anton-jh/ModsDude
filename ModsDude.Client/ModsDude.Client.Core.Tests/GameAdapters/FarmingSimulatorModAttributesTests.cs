using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.Models;
using System.IO.Compression;
using System.Text;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// What a Farming Simulator archive is tagged with, read through a real folder of real archives for
/// the same reason the adapter's other tests are: every answer comes from the zip's contents.
/// </summary>
public class FarmingSimulatorModAttributesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("modsdude-attribute-tests").FullName;


    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Directory.Delete(_folder, true);
    }


    [Fact]
    public async Task Store_items_give_their_categories_brands_and_kinds()
    {
        WriteMod("FS25_Pack",
            storeItems:
            [
                ("vehicles/tractor.xml", Vehicle("tractorsM", "JOHNDEERE")),
                ("placeables/shed.xml", Placeable("sheds", "NONE"))
            ]);

        var mod = await ScanOne();

        Assert.Equal(["tractorsM", "sheds"], Values(mod, "category"));
        Assert.Equal(["johndeere"], Values(mod, "brand"));
        Assert.Equal(["vehicle", "placeable"], Values(mod, "kind"));
    }

    /// <summary>
    /// The game lets one store item sit in several shop categories, space-separated in one field, and
    /// a pack lists the same category once per item. Each one is a tag, once.
    /// </summary>
    [Fact]
    public async Task A_category_field_naming_several_is_split_and_repeats_are_dropped()
    {
        WriteMod("FS25_Pack",
            storeItems:
            [
                ("a.xml", Vehicle("trailers manureSpreaders", "FLIEGL")),
                ("b.xml", Vehicle("trailers", "fliegl"))
            ]);

        var mod = await ScanOne();

        Assert.Equal(["trailers", "manureSpreaders"], Values(mod, "category"));
        Assert.Equal(["fliegl"], Values(mod, "brand"));
    }

    /// <summary>
    /// The game reads categories case-insensitively and authors rely on it. One category is one tag.
    /// </summary>
    [Fact]
    public async Task A_known_category_in_another_casing_is_spelled_the_games_way()
    {
        WriteMod("FS22_Pack",
            storeItems:
            [
                ("a.xml", Placeable("PlaceableMisc", "NONE")),
                ("b.xml", Placeable("placeableMisc", "NONE")),
                ("c.xml", Placeable("myOwnCategory", "NONE"))
            ]);

        var mod = await ScanOne();

        Assert.Equal(["placeableMisc", "myOwnCategory"], Values(mod, "category"));
    }

    [Fact]
    public async Task An_FS25_mod_is_tagged_with_the_shop_sections_of_its_categories()
    {
        WriteMod("FS25_Pack",
            storeItems:
            [
                ("a.xml", Placeable("sheds", "NONE")),
                ("b.xml", Vehicle("tractorsM tractorsL", "FENDT")),
                ("c.xml", Placeable("myOwnCategory", "NONE"))
            ]);

        var mod = await ScanOne();

        // Once each, in shop order, and nothing guessed for the category no section lists.
        Assert.Equal(["drivables", "placeable"], Values(mod, "categoryGroup"));
    }

    /// <summary>FS22 ships no category file, so there is nothing to read its sections from.</summary>
    [Fact]
    public async Task An_FS22_mod_has_no_category_group()
    {
        WriteMod("FS22_Tractor", storeItems: [("a.xml", Vehicle("tractorsM", "FENDT"))]);

        var mod = Assert.Single(await new FarmingSimulatorBaseModAdapter(FarmingSimulatorGameVersion.Fs22).GetModsFromFolder(_folder, CancellationToken.None));

        Assert.Empty(Values(mod, "categoryGroup"));
        Assert.DoesNotContain(new FarmingSimulatorBaseModAdapter(FarmingSimulatorGameVersion.Fs22).Attributes, x => x.Key == "categoryGroup");
    }

    /// <summary>The groups are a second copy of the SDK file; this is what keeps the two lists one.</summary>
    [Fact]
    public void Every_FS25_category_is_in_exactly_one_group()
    {
        var categories = new FarmingSimulatorBaseModAdapter(FarmingSimulatorGameVersion.Fs25).Attributes.Single(x => x.Key == "category").Values;

        Assert.Equal(
            categories.Order(StringComparer.OrdinalIgnoreCase),
            FarmingSimulatorModAttributes.Fs25GroupOf.Keys.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Script_and_map_mods_say_so()
    {
        WriteMod("FS25_Script", descBody: "<extraSourceFiles><sourceFile filename=\"main.lua\"/></extraSourceFiles>");
        WriteMod("FS25_Map", descBody: "<maps><map id=\"m\"/></maps>");

        var mods = (await Scan()).ToDictionary(x => x.Id.Value);

        Assert.Equal(["script"], Values(mods["fs25_script"], "kind"));
        Assert.Equal(["map"], Values(mods["fs25_map"], "kind"));
    }

    [Fact]
    public async Task A_hand_tool_is_its_own_kind()
    {
        WriteMod("FS25_Saw", storeItems: [("saw.xml", Store("handTool", "chainsaws", "STIHL"))]);

        var mod = await ScanOne();

        Assert.Equal(["handtool"], Values(mod, "kind"));
    }

    [Theory]
    [InlineData("true", "yes")]
    [InlineData("false", "no")]
    [InlineData("TRUE", "yes")]
    public async Task Multiplayer_support_is_yes_or_no(string declared, string expected)
    {
        WriteMod("FS25_Thing", multiplayer: declared);

        var mod = await ScanOne();

        Assert.Equal([expected], Values(mod, "multiplayer"));
    }

    [Fact]
    public async Task A_mod_that_does_not_say_whether_it_plays_in_multiplayer_is_not_tagged_either_way()
    {
        WriteMod("FS25_Thing", multiplayer: null);

        var mod = await ScanOne();

        Assert.Empty(Values(mod, "multiplayer"));
    }

    /// <summary>
    /// Maps and packs point store items at the base game's own files. Those are not in the archive
    /// and not this mod's to read.
    /// </summary>
    [Fact]
    public async Task Store_items_in_the_base_game_are_skipped()
    {
        WriteMod("FS22_Trees",
            storeItems: [("vehicles/cart.xml", Vehicle("trailers", "LIZARD"))],
            extraStoreItemPaths: ["$data/maps/trees/birch/birch_stage01.xml"]);

        var mod = await ScanOne();

        Assert.Equal(["trailers"], Values(mod, "category"));
    }

    /// <summary>
    /// The game loads a mod with one broken store XML in it and skips that item, so the scan does
    /// the same rather than losing the whole mod - or even its other tags.
    /// </summary>
    [Fact]
    public async Task A_broken_or_missing_store_item_costs_only_its_own_tags()
    {
        WriteMod("FS25_Pack",
            storeItems:
            [
                ("good.xml", Vehicle("mowers", "KUHN")),
                ("broken.xml", "<vehicle><storeData><category>cutters")
            ],
            extraStoreItemPaths: ["missing.xml"]);

        var mod = await ScanOne();

        Assert.Equal(["mowers"], Values(mod, "category"));
        Assert.Equal(["kuhn"], Values(mod, "brand"));
    }

    [Fact]
    public async Task A_store_item_path_is_found_whatever_its_casing()
    {
        WriteMod("FS25_Pack",
            storeItems: [("Vehicles/Tractor.xml", Vehicle("tractorsS", "FENDT"))],
            storeItemPathOverride: "vehicles\\tractor.XML");

        var mod = await ScanOne();

        Assert.Equal(["tractorsS"], Values(mod, "category"));
    }

    [Theory]
    [InlineData(FarmingSimulatorGameVersion.Fs22)]
    [InlineData(FarmingSimulatorGameVersion.Fs25)]
    public async Task Every_key_reported_is_one_the_adapter_declared(FarmingSimulatorGameVersion gameVersion)
    {
        WriteMod("FS25_Everything",
            storeItems:
            [
                ("a.xml", Vehicle("tractorsM", "CLAAS")),
                ("b.xml", Placeable("sheds", "NONE")),
                ("c.xml", Store("handTool", "chainsaws", "STIHL"))
            ],
            descBody: "<extraSourceFiles><sourceFile filename=\"main.lua\"/></extraSourceFiles><maps><map id=\"m\"/></maps>");

        var adapter = new FarmingSimulatorBaseModAdapter(gameVersion);
        var mod = Assert.Single(await adapter.GetModsFromFolder(_folder, CancellationToken.None));

        var declared = adapter.Attributes.Select(x => x.Key).ToHashSet();

        Assert.NotEmpty(mod.Attributes);
        Assert.All(mod.Attributes, x => Assert.Contains(x.Key, declared));
    }

    [Fact]
    public void Category_and_multiplayer_have_short_aliases()
    {
        var attributes = new FarmingSimulatorBaseModAdapter(FarmingSimulatorGameVersion.Fs25).Attributes;

        Assert.True(attributes.Single(x => x.Key == "category").IsNamed("cat"));
        Assert.True(attributes.Single(x => x.Key == "multiplayer").IsNamed("MP"));
        Assert.True(attributes.Single(x => x.Key == "categoryGroup").IsNamed("catGroup"));
    }

    [Fact]
    public void Each_game_declares_its_own_categories()
    {
        static IReadOnlyList<string> Categories(FarmingSimulatorGameVersion version)
            => new FarmingSimulatorBaseModAdapter(version).Attributes.Single(x => x.Key == "category").Values;

        Assert.Contains("woodHarvesting", Categories(FarmingSimulatorGameVersion.Fs22));
        Assert.DoesNotContain("woodHarvesting", Categories(FarmingSimulatorGameVersion.Fs25));
        Assert.Contains("forestryHarvesters", Categories(FarmingSimulatorGameVersion.Fs25));
    }


    private static IReadOnlyList<string?> Values(LocalMod mod, string key)
        => [.. mod.Attributes.Where(x => x.Key == key).Select(x => x.Value)];

    private static string Vehicle(string category, string brand) => Store("vehicle", category, brand);

    private static string Placeable(string category, string brand) => Store("placeable", category, brand);

    private static string Store(string root, string category, string brand) => $"""
        <?xml version="1.0" encoding="utf-8" standalone="no"?>
        <{root} type="x">
            <storeData>
                <name>Thing</name>
                <category>{category}</category>
                <brand>{brand}</brand>
            </storeData>
        </{root}>
        """;

    private async Task<LocalMod> ScanOne() => Assert.Single(await Scan());

    private async Task<IReadOnlyList<LocalMod>> Scan()
        => [.. await new FarmingSimulatorBaseModAdapter(FarmingSimulatorGameVersion.Fs25).GetModsFromFolder(_folder, CancellationToken.None)];

    /// <param name="storeItemPathOverride">What modDesc calls the single store item, where that differs from the entry's name.</param>
    private void WriteMod(
        string id,
        (string Path, string Xml)[]? storeItems = null,
        string[]? extraStoreItemPaths = null,
        string? storeItemPathOverride = null,
        string? multiplayer = "true",
        string descBody = "")
    {
        storeItems ??= [];

        var listed = storeItemPathOverride is null
            ? storeItems.Select(x => x.Path)
            : [storeItemPathOverride];

        var storeItemsElement = storeItems.Length + (extraStoreItemPaths?.Length ?? 0) > 0
            ? $"<storeItems>{string.Concat(listed.Concat(extraStoreItemPaths ?? []).Select(x => $"<storeItem xmlFilename=\"{x}\"/>"))}</storeItems>"
            : string.Empty;

        var multiplayerElement = multiplayer is null ? string.Empty : $"<multiplayer supported=\"{multiplayer}\"/>";

        var modDesc = $"""
            <?xml version="1.0" encoding="utf-8" standalone="no"?>
            <modDesc descVersion="95">
                <author>Someone</author>
                <version>1.0.0.0</version>
                <title><en>{id}</en></title>
                <description><en>A mod.</en></description>
                {multiplayerElement}
                {storeItemsElement}
                {descBody}
            </modDesc>
            """;

        using var file = File.Create(Path.Combine(_folder, $"{id}.zip"));
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        using (var entry = archive.CreateEntry("modDesc.xml").Open())
        {
            entry.Write(Encoding.UTF8.GetBytes(modDesc));
        }

        foreach (var (path, xml) in storeItems)
        {
            using var entry = archive.CreateEntry(path).Open();
            entry.Write(Encoding.UTF8.GetBytes(xml));
        }
    }
}
