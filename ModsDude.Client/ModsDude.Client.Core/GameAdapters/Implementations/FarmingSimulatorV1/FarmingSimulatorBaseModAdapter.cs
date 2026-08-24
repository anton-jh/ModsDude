using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

public class FarmingSimulatorBaseModAdapter : IBaseModAdapter
{
    private static readonly string[] _imageExtensions = [".dds", ".png", ".jpg", ".jpeg"];


    public Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken)
    {
        // Each file gets its own archive handle, so reading them in parallel is safe, and a mod
        // folder can hold well over a thousand archives. The degree of parallelism is capped
        // deliberately: this is disk bound, so a handful at a time is as quick as hundreds, and
        // queueing one work item per file would hand the whole thread pool - which the rest of the
        // app shares - to the scan for as long as it runs.
        return Task.Run<IEnumerable<LocalMod>>(() =>
        {
            var files = Directory.EnumerateFiles(path).ToList();
            var mods = new LocalMod?[files.Count];

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            // Indexed rather than collected, so the results keep the order of the folder.
            Parallel.For(0, files.Count, options, i => mods[i] = GetModFromFile(files[i], cancellationToken));

            return mods.OfType<LocalMod>().ToList();
        }, cancellationToken);
    }

    private static LocalMod? GetModFromFile(string path, CancellationToken cancellationToken)
    {
        using var zip = GetZip(path);
        if (zip is null) return null;

        var maybeDesc = GetModDesc(zip, cancellationToken);
        if (maybeDesc.HasValue is false) return null;

        var desc = maybeDesc.Value;

        var maybeLocalMod =
            from filename in Maybe.From(Path.GetFileNameWithoutExtension(path))
            from version in Maybe.From(desc.Element("version")?.Value)
            from titleGroup in Maybe.From(desc.Element("title"))
            from title in GetEnglishOrFallback(titleGroup, filename)
            from descriptionGroup in Maybe.From(desc.Element("description"))
            from description in GetEnglishOrFallback(descriptionGroup, "")
            select new LocalMod(filename, version, title, NormalizeDescription(description), () => File.OpenRead(path))
            {
                Author = desc.Element("author")?.Value.Trim(),
                Icon = GetIcon(zip, path, desc),
                Images = GetImages(zip, path)
            };

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

    private static Maybe<XElement> GetModDesc(ZipArchive zip, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = zip.GetEntry("modDesc.xml");
        if (entry is null) return Maybe<XElement>.None;

        using var xmlStream = entry.Open();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(xmlStream, settings);
        var document = XDocument.Load(reader);
        return Maybe.From(document.Element("modDesc"));
    }

    private static Maybe<string> GetEnglishOrFallback(XElement element, string fallback)
    {
        return Maybe.From(element.Element("en")?.Value
            ?? element.Elements().FirstOrDefault()?.Value
            ?? fallback);
    }

    /// <summary>
    /// Descriptions are CDATA blocks inside the xml, so every line arrives with the surrounding
    /// element's indentation baked in.
    /// </summary>
    private static string NormalizeDescription(string description)
    {
        var lines = description.Replace("\r\n", "\n").Split('\n')
            .Select(x => x.TrimEnd())
            .ToList();

        var indent = lines
            .Where(x => string.IsNullOrWhiteSpace(x) is false)
            .Select(x => x.Length - x.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        var builder = new StringBuilder();
        var blankRun = 0;

        foreach (var line in lines.Select(x => x.Length >= indent ? x[indent..] : x.TrimStart()))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankRun++;
                if (blankRun > 1 || builder.Length == 0) continue;
            }
            else
            {
                blankRun = 0;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// The icon declared in modDesc.xml regularly names an extension the archive doesn't actually
    /// contain (".png" declared, ".dds" shipped), so fall back to matching on the name alone.
    /// </summary>
    private static LocalModImage? GetIcon(ZipArchive zip, string modPath, XElement desc)
    {
        var declared = desc.Element("iconFilename")?.Value;

        var entry = FindImageEntry(zip, declared)
            ?? zip.Entries.FirstOrDefault(x => x.Name.StartsWith("icon_", StringComparison.OrdinalIgnoreCase) && IsImage(x.Name));

        return entry is null ? null : CreateImage(modPath, entry);
    }

    /// <summary>
    /// The store images - the ones the in-game shop shows. A mod ships anywhere from none of them
    /// (script mods) to a few dozen (vehicle packs).
    /// </summary>
    private static IReadOnlyList<LocalModImage> GetImages(ZipArchive zip, string modPath)
    {
        return zip.Entries
            .Where(x => x.Name.StartsWith("store_", StringComparison.OrdinalIgnoreCase) && IsImage(x.Name))
            .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(x => CreateImage(modPath, x))
            .ToList();
    }

    private static ZipArchiveEntry? FindImageEntry(ZipArchive zip, string? declaredName)
    {
        if (string.IsNullOrWhiteSpace(declaredName)) return null;

        var normalized = declaredName.Replace('\\', '/').TrimStart('/');

        var exact = zip.GetEntry(normalized);
        if (exact is not null) return exact;

        var withoutExtension = StripExtension(normalized);

        return zip.Entries.FirstOrDefault(x =>
            IsImage(x.Name) &&
            string.Equals(StripExtension(x.FullName.Replace('\\', '/')), withoutExtension, StringComparison.OrdinalIgnoreCase));
    }

    private static LocalModImage CreateImage(string modPath, ZipArchiveEntry entry)
    {
        // The entry belongs to an archive that is about to be closed - capture the name instead.
        var entryName = entry.FullName;
        var cacheKey = $"{modPath}|{entryName}|{entry.Length}|{entry.Crc32}";

        return new LocalModImage(entry.Name, cacheKey, async cancellationToken =>
        {
            await using var stream = new FileStream(modPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var target = archive.GetEntry(entryName)
                ?? throw new FileNotFoundException($"'{entryName}' is no longer present in '{modPath}'.");

            await using var entryStream = target.Open();
            using var buffer = new MemoryStream((int)target.Length);
            await entryStream.CopyToAsync(buffer, cancellationToken);

            return buffer.ToArray();
        });
    }

    private static bool IsImage(string name)
        => _imageExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    private static string StripExtension(string name)
    {
        var lastDot = name.LastIndexOf('.');
        var lastSlash = name.LastIndexOf('/');

        return lastDot > lastSlash ? name[..lastDot] : name;
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
