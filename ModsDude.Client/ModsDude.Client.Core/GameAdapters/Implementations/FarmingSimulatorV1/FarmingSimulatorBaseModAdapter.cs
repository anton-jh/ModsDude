using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

public class FarmingSimulatorBaseModAdapter : IBaseModAdapter
{
    public async Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(path);
        var modTasks = files.Select(x => GetModFromFile(x, cancellationToken));
        var mods = await Task.WhenAll(modTasks);

        return mods.OfType<LocalMod>();
    }

    private static async Task<LocalMod?> GetModFromFile(string path, CancellationToken cancellationToken)
    {
        var maybeLocalMod =
            from desc in await GetModDesc(path, cancellationToken)
            from filename in Maybe.From(Path.GetFileNameWithoutExtension(path))
            from version in Maybe.From(desc.Element("version")?.Value)
            from titleGroup in Maybe.From(desc.Element("title"))
            from title in GetEnglishOrFallback(titleGroup, filename)
            from descriptionGroup in Maybe.From(desc.Element("description"))
            from description in GetEnglishOrFallback(descriptionGroup, "")
            select new LocalMod(filename, version, title, description, () => File.OpenRead(path));
        
        return maybeLocalMod.HasValue ? maybeLocalMod.Value : null;
    }

    private static ZipArchive? GetZip(string path)
    {
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static async Task<Maybe<XElement>> GetModDesc(string path, CancellationToken cancellationToken)
    {
        var zip = GetZip(path);
        if (zip is null) return Maybe<XElement>.None;

        var entry = zip.GetEntry("modDesc.xml");
        if (entry is null) return Maybe<XElement>.None;

        await using var xmlStream = entry.Open();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(xmlStream, settings);
        var document = await Task.Run(() => XDocument.Load(reader), cancellationToken);
        return Maybe.From(document.Element("modDesc"));
    }

    private static Maybe<string> GetEnglishOrFallback(XElement element, string fallback)
    {
        return Maybe.From(element.Element("en")?.Value
            ?? element.Elements().FirstOrDefault()?.Value
            ?? fallback);
    }

    public IInstanceModAdapter WithInstanceSettings(string serializedInstanceSettings)
    {
        var instanceSettings = FarmingSimulatorInstanceSettings.Deserialize(serializedInstanceSettings);
        instanceSettings.EnsureValid();
        return new FarmingSimulatorInstanceModAdapter(instanceSettings);
    }

    public IInstanceModAdapter WithInstanceSettings(DynamicForm instanceSettings)
    {
        if (instanceSettings is not FarmingSimulatorInstanceSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorInstanceSettings>(instanceSettings);
        }
        settings.EnsureValid();
        return new FarmingSimulatorInstanceModAdapter(settings);
    }
}


public class FarmingSimulatorInstanceModAdapter(FarmingSimulatorInstanceSettings instanceSettings)
    : FarmingSimulatorBaseModAdapter, IInstanceModAdapter
{
    public async Task<IEnumerable<LocalMod>> GetInstalledMods(CancellationToken cancellationToken)
    {
        var maybe =
            from gameDataFolderPath in Maybe.From(instanceSettings.GameDataFolder)
            select GetModsFromFolder(Path.Combine(gameDataFolderPath, "mods"), cancellationToken);

        return await maybe.GetValueOrDefault(Task.FromResult(Enumerable.Empty<LocalMod>()));
    }
}
