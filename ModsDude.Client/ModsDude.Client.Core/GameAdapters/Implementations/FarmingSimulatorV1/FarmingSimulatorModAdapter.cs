using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

public class FarmingSimulatorModAdapter : ModAdapterBase<FarmingSimulatorBaseSettings, FarmingSimulatorInstanceSettings>
{
    public override async Task<IEnumerable<LocalMod>> GetModsFromFolder(string path)
    {
        var files = Directory.GetFiles(path);
        var modTasks = files.Select(GetModFromFile);
        var mods = await Task.WhenAll(modTasks);

        return mods.OfType<LocalMod>();
    }

    public override async Task<IEnumerable<LocalMod>> GetModsFromInstalled(FarmingSimulatorInstanceSettings instanceSettings)
    {
        var maybe =
            from gameDataFolderPath in Maybe.From(instanceSettings.GameDataFolder)
            select GetModsFromFolder(Path.Combine(gameDataFolderPath, "mods"));

        return await maybe.GetValueOrDefault(Task.FromResult(Enumerable.Empty<LocalMod>()));
    }

    private async Task<LocalMod?> GetModFromFile(string path)
    {
        var maybeLocalMod =
            from desc in await GetModDesc(path)
            from filename in Maybe.From(Path.GetFileNameWithoutExtension(path))
            from version in Maybe.From(desc.Element("version")?.Value)
            from titleGroup in Maybe.From(desc.Element("title"))
            from title in GetEnglishOrFallback(titleGroup, filename)
            from descriptionGroup in Maybe.From(desc.Element("description"))
            from description in GetEnglishOrFallback(descriptionGroup, "")
            select new LocalMod(filename, version, title, description, () => File.OpenRead(path));
        
        return maybeLocalMod.HasValue ? maybeLocalMod.Value : null;
    }

    private static async Task<ZipArchive?> GetZip(string path)
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

    private static async Task<Maybe<XElement>> GetModDesc(string path)
    {
        var zip = await GetZip(path);
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
        var document = await Task.Run(() => XDocument.Load(reader));
        return Maybe.From(document.Element("modDesc"));
    }

    private static Maybe<string> GetEnglishOrFallback(XElement element, string fallback)
    {
        return Maybe.From(element.Element("en")?.Value
            ?? element.Elements().FirstOrDefault()?.Value
            ?? fallback);
    }
}
