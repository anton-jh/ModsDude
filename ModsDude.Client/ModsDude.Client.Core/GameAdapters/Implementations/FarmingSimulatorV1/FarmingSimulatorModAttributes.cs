using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

/// <summary>
/// What a Farming Simulator mod is tagged with, and every tag it can be: the shop categories, their
/// shop sections and brands of its store items, what kind of thing it adds, and whether it plays in
/// multiplayer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raw ids, not labels.</b> A category is <c>tractorsM</c>, as the store item spells it, and a
/// brand is its id lower-cased. The game's own titles live in its encrypted archives, so a label
/// would be text somebody wrote rather than what the shop says - and a mod-defined brand's title
/// exists only inside the mod that defines it, which a teammate without the file cannot read.
/// </para>
/// <para>
/// <b>The category list is the game's, not ours.</b> FS25's is the SDK's <c>storeCategories.xml</c>
/// (<c>sdk/xmlDoku</c> in the install), in full. A category missing from it is still reported - the
/// list only feeds a search box, which also offers whatever the catalog holds.
/// </para>
/// </remarks>
internal static class FarmingSimulatorModAttributes
{
    public const string CategoryKey = "category";
    public const string CategoryGroupKey = "categoryGroup";
    public const string BrandKey = "brand";
    public const string KindKey = "kind";
    public const string MultiplayerKey = "multiplayer";

    public const string Vehicle = "vehicle";
    public const string Placeable = "placeable";
    public const string HandTool = "handtool";
    public const string Script = "script";
    public const string Map = "map";

    /// <summary>What the store items say when they have no brand. Not a brand, so never reported.</summary>
    private const string _noBrand = "none";

    private static readonly string[] _kinds = [Vehicle, Placeable, HandTool, Script, Map];

    /// <summary>A store item's root element, and the kind it makes the mod.</summary>
    private static readonly Dictionary<string, string> _storeItemKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vehicle"] = Vehicle,
        ["placeable"] = Placeable,
        ["handTool"] = HandTool
    };

    private static readonly string[] _fs25Categories =
    [
        "animalpens", "animalTransport", "augerWagons", "baleLoaders", "balersRound", "balersSquare",
        "bales", "baleWrappers", "balingMisc", "barrels", "beeHives", "beetHarvesterCutters",
        "beetHarvesters", "beetLoading", "belts", "bigbagPallets", "bigbags", "cars", "carTrailers",
        "chainsaws", "combineWindrower", "containers", "cornHeaders", "cottonHarvesters",
        "cottonTransport", "cultivators", "cutters", "cutterTrailers", "decoration", "dieselTanks",
        "discHarrows", "farmhouses", "fences", "fertilizerSpreaders", "fillableTanks", "flashlights",
        "floodLighting", "forageHarvesterCutters", "forageHarvesterCutterTrailers", "forageHarvesters",
        "forageMixers", "forestryExcavators", "forestryExcavatorTools", "forestryForwarders",
        "forestryHarvesters", "forestryMisc", "forestryMulchers", "forestryPlanters",
        "forestryStumpCutters", "forestryWinches", "forklifts", "frontLoaders", "frontLoaderTools",
        "frontLoaderVehicles", "gardenSheds", "generators", "grapeHarvesters", "grapeTools",
        "grapeTrailers", "grasslandCare", "greenBeanHarvesters", "handtoolsAnimals", "handtoolsMisc",
        "harvesters", "ibc", "leveler", "loaderWagons", "lowloaders", "manureSpreaders",
        "markingSpray", "misc", "miscDrivables", "mowers", "mulchers", "objectAnimal", "objectMisc",
        "oliveHarvesters", "palletAnimals", "palletBaling", "palletFertilizerHerbicide",
        "palletForestry", "palletGrassland", "pallets", "palletSeeds", "palletSeedsRootcrops",
        "palletSilage", "palletVegetables", "peaHarvesters", "placeableMisc", "planters", "plows",
        "potatoHarvesting", "potatoPlanting", "powerHarrows", "productionPoints", "riceHarvesters",
        "ricePlanters", "rollers", "seeders", "seedTanks", "sellingPoints", "sheds",
        "shippingContainers", "shovels", "silocompaction", "siloExtensions", "silos", "skidSteerTools",
        "skidSteerVehicles", "slurryTanks", "slurryTools", "slurryTransport", "spaders",
        "specialCropsPallets", "specialHeaders", "spinachHarvesters", "sprayers", "stonePickers",
        "storages", "strawBlowers", "subsoilers", "sugarcaneHarvesters", "sugarcanePlanters",
        "sugarcaneTransport", "tedders", "teleLoaderTools", "teleLoaderVehicles", "tractorsL",
        "tractorsM", "tractorsS", "trailers", "trailersChangingSystem", "trailersFlatbed",
        "trailersSemi", "trees", "trucks", "vegetableHarvesters", "vegetablePlanters", "waterTanks",
        "weeders", "weights", "wheelLoaderTools", "wheelLoaderVehicles", "windrowers",
        "winterEquipment", "woodChippers", "woodTransport"
    ];

    /// <summary>
    /// The shop section each FS25 category sits in - the SDK's category <c>type</c>, lower-cased.
    /// In shop order.
    /// </summary>
    private static readonly (string Group, string[] Categories)[] _fs25CategoryGroups =
    [
        ("drivables", ["tractorsS", "tractorsM", "tractorsL", "trucks", "cars", "miscDrivables"]),
        ("loaders", ["frontLoaderVehicles", "frontLoaders", "frontLoaderTools", "teleLoaderVehicles", "teleLoaderTools", "wheelLoaderVehicles", "wheelLoaderTools", "skidSteerVehicles", "skidSteerTools", "forklifts"]),
        ("trailers", ["trailers", "augerWagons", "trailersChangingSystem", "trailersFlatbed", "lowloaders", "trailersSemi"]),
        ("soil_preparation", ["plows", "cultivators", "discHarrows", "powerHarrows", "subsoilers", "mulchers", "spaders", "stonePickers"]),
        ("seeding", ["seeders", "planters", "seedTanks", "palletSeeds"]),
        ("yield", ["sprayers", "manureSpreaders", "fertilizerSpreaders", "slurryTanks", "slurryTools", "slurryTransport", "weeders", "rollers", "palletFertilizerHerbicide"]),
        ("combine", ["harvesters", "cutters", "cornHeaders", "specialHeaders", "cutterTrailers", "combineWindrower"]),
        ("forage", ["forageHarvesters", "forageHarvesterCutters", "forageHarvesterCutterTrailers", "leveler", "silocompaction", "palletSilage"]),
        ("grassland", ["mowers", "tedders", "windrowers", "loaderWagons", "grasslandCare", "palletGrassland"]),
        ("baling", ["balersSquare", "balersRound", "baleLoaders", "baleWrappers", "balingMisc", "palletBaling"]),
        ("rootcrops", ["beetHarvesters", "beetHarvesterCutters", "beetLoading", "potatoPlanting", "potatoHarvesting", "palletSeedsRootcrops"]),
        ("vegetables", ["vegetablePlanters", "vegetableHarvesters", "spinachHarvesters", "greenBeanHarvesters", "peaHarvesters", "palletVegetables"]),
        ("specialcrops", ["ricePlanters", "riceHarvesters", "sugarcanePlanters", "sugarcaneHarvesters", "sugarcaneTransport", "cottonHarvesters", "cottonTransport", "specialCropsPallets"]),
        ("grapes_olives", ["oliveHarvesters", "grapeHarvesters", "grapeTrailers", "grapeTools"]),
        ("animals", ["forageMixers", "barrels", "animalTransport", "strawBlowers", "palletAnimals"]),
        ("forestry", ["forestryHarvesters", "forestryForwarders", "forestryExcavators", "forestryExcavatorTools", "woodTransport", "woodChippers", "forestryMulchers", "forestryWinches", "forestryPlanters", "forestryStumpCutters", "forestryMisc", "palletForestry"]),
        ("misc", ["weights", "belts", "winterEquipment", "carTrailers", "misc"]),
        ("objects", ["bigbags", "bigbagPallets", "ibc", "pallets", "bales", "shippingContainers", "objectAnimal", "objectMisc"]),
        ("handtools", ["chainsaws", "shovels", "flashlights", "markingSpray", "handtoolsAnimals", "handtoolsMisc"]),
        ("placeable", ["animalpens", "fences", "trees", "storages", "containers", "dieselTanks", "waterTanks", "fillableTanks", "silos", "siloExtensions", "sheds", "gardenSheds", "farmhouses", "beeHives", "generators", "floodLighting", "decoration", "productionPoints", "sellingPoints", "placeableMisc"])
    ];

    private static readonly string[] _fs25Groups = [.. _fs25CategoryGroups.Select(x => x.Group)];

    /// <summary>Each FS25 category's group. Exposed for the test that holds it to the category list.</summary>
    internal static IReadOnlyDictionary<string, string> Fs25GroupOf => _fs25GroupOf;

    private static readonly Dictionary<string, string> _fs25GroupOf = _fs25CategoryGroups
        .SelectMany(x => x.Categories.Select(category => (Category: category, x.Group)))
        .ToDictionary(x => x.Category, x => x.Group, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<ModAttributeDefinition> _fs25 = Declare(_fs25Categories, _fs25Groups);

    /// <summary>
    /// Every category the game knows, by any casing, to the casing the game spells it in. The game
    /// matches categories case-insensitively and mod authors take advantage - <c>PlaceableMisc</c>,
    /// <c>lowLoaders</c> - so without this one category would be two tags.
    /// </summary>
    private static readonly Dictionary<string, string> _canonicalCategories = _fs25Categories
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToDictionary(x => x, x => x, StringComparer.OrdinalIgnoreCase);


    /// <summary>Every key the adapter reports for this game - and it reports nothing else.</summary>
    public static IReadOnlyList<ModAttributeDefinition> For(FarmingSimulatorGameVersion gameVersion) => gameVersion switch
    {
        FarmingSimulatorGameVersion.Fs25 => _fs25,
        _ => throw new ArgumentOutOfRangeException(nameof(gameVersion), gameVersion, "No mod attributes are declared for this game.")
    };

    private static IReadOnlyList<ModAttributeDefinition> Declare(string[] categories, string[] groups) =>
    [
        new(CategoryKey, ["cat"], categories),
        new(CategoryGroupKey, ["catGroup"], groups),
        new(BrandKey, [], []),
        new(KindKey, [], _kinds),
        new(MultiplayerKey, ["mp"], ["yes", "no"])
    ];


    /// <summary>
    /// Reads one mod's attributes from its <c>modDesc</c> and the store items it names.
    /// </summary>
    /// <remarks>
    /// <b>A store item that cannot be read costs its own tags and nothing else.</b> A mod is still a
    /// mod with one broken store XML in it - the game loads the rest - so an unparseable item is
    /// skipped rather than thrown, and one pointing into the base game (<c>$data/...</c>) is not
    /// this archive's to read.
    /// </remarks>
    /// <param name="gameVersion">
    /// Which game's tags these are. The category groups are FS25's, so <c>categoryGroup</c> is the one
    /// thing here that depends on it.
    /// </param>
    public static IReadOnlyList<ModAttribute> Read(ZipArchive zip, XElement desc, FarmingSimulatorGameVersion gameVersion, CancellationToken cancellationToken)
    {
        var categories = new List<string>();
        var brands = new List<string>();
        var kinds = new HashSet<string>();

        foreach (var storeItem in ReadStoreItems(zip, desc, cancellationToken))
        {
            if (_storeItemKinds.TryGetValue(storeItem.Name.LocalName, out var kind))
            {
                kinds.Add(kind);
            }

            var storeData = storeItem.Element("storeData");

            categories.AddRange(Words(storeData?.Element("category")?.Value)
                .Select(x => _canonicalCategories.GetValueOrDefault(x, x)));

            if (storeData?.Element("brand")?.Value.Trim().ToLowerInvariant() is { Length: > 0 } brand && brand != _noBrand)
            {
                brands.Add(brand);
            }
        }

        if (desc.Element("extraSourceFiles")?.Elements("sourceFile").Any() is true)
        {
            kinds.Add(Script);
        }

        if (desc.Element("maps")?.Elements("map").Any() is true)
        {
            kinds.Add(Map);
        }

        var attributes = new List<ModAttribute>();

        attributes.AddRange(categories.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => new ModAttribute(CategoryKey, x)));

        if (gameVersion is FarmingSimulatorGameVersion.Fs25)
        {
            // A category no group lists - one a mod invented - has no group, rather than a guessed one.
            var groups = categories.Select(x => _fs25GroupOf.GetValueOrDefault(x)).OfType<string>().ToHashSet();

            attributes.AddRange(_fs25Groups.Where(groups.Contains).Select(x => new ModAttribute(CategoryGroupKey, x)));
        }

        attributes.AddRange(brands.Distinct(StringComparer.Ordinal).Select(x => new ModAttribute(BrandKey, x)));
        // Declared order rather than the order the items happened to be listed in, so two versions
        // with the same contents tag themselves identically.
        attributes.AddRange(_kinds.Where(kinds.Contains).Select(x => new ModAttribute(KindKey, x)));

        if (bool.TryParse(desc.Element("multiplayer")?.Attribute("supported")?.Value, out var multiplayer))
        {
            attributes.Add(new ModAttribute(MultiplayerKey, multiplayer ? "yes" : "no"));
        }

        return attributes;
    }

    private static IEnumerable<XElement> ReadStoreItems(ZipArchive zip, XElement desc, CancellationToken cancellationToken)
    {
        var paths = desc.Element("storeItems")?.Elements("storeItem")
            .Select(x => x.Attribute("xmlFilename")?.Value.Replace('\\', '/').TrimStart('/'))
            .OfType<string>()
            .Where(x => x.Length > 0 && x.StartsWith('$') is false)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths is not { Count: > 0 })
        {
            yield break;
        }

        // The game resolves these on a case-insensitive filesystem, and authors spell them however
        // they like - so the archive's own casing cannot be relied on to match.
        var entries = zip.Entries
            .GroupBy(x => x.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entries.TryGetValue(path, out var entry) && TryLoad(entry) is XElement root)
            {
                yield return root;
            }
        }
    }

    private static XElement? TryLoad(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();

            return SafeXml.Load(stream).Root;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>A category field can name several, space-separated: <c>trailers manureSpreaders</c>.</summary>
    private static IEnumerable<string> Words(string? value)
        => value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
}
