using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

public class FarmingSimulatorBaseModAdapter(ILoggerFactory? loggerFactory = null) : IBaseModAdapter
{
    private static readonly string[] _imageExtensions = [".dds", ".png", ".jpg", ".jpeg"];

    /// <summary>
    /// A folder scan skips what it cannot read, which is correct and also silent - in a mod folder
    /// every skip is a mod that has vanished from the catalog. Debug, because in Downloads most
    /// files are not mods and this would say so a thousand times; that is exactly the level to turn
    /// on when a mod is missing and nobody can say why.
    /// </summary>
    protected ILogger Log { get; } = loggerFactory?.CreateLogger<FarmingSimulatorBaseModAdapter>()
        ?? NullLogger<FarmingSimulatorBaseModAdapter>.Instance;

    protected ILoggerFactory? Loggers { get; } = loggerFactory;


    /// <summary>
    /// Declared false rather than inherited, to say that the answer is unknown rather than assumed.
    /// Farming Simulator downloads and updates mods from inside the game, and whether that updater
    /// rewrites an archive in place - which through a hardlink would corrupt a store blob shared
    /// with every repo on the volume - has not been tested against the real game. That costs the
    /// main game its fast path for now, which is the right way round.
    /// </summary>
    public bool SupportsHardlinks => false;


    /// <summary>
    /// What a Farming Simulator mod is packaged as. Anything else in the folder is not a mod that
    /// failed to read - it is not a candidate at all, and telling those two apart is the whole point
    /// of filtering here rather than finding out by trying to open it.
    /// </summary>
    /// <remarks>
    /// A mod folder legitimately holds files that are none of the app's business: Farming Simulator
    /// keeps a <c>mods.json</c> beside the archives, and a source can be any folder the user points
    /// at. Those are ignored in silence, because there is nothing wrong with them.
    /// </remarks>
    private static readonly string[] _modArchiveExtensions = [".zip"];


    public Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken)
    {
        // Each file gets its own archive handle, so reading them in parallel is safe, and a mod
        // folder can hold well over a thousand archives. The degree of parallelism is capped
        // deliberately: this is disk bound, so a handful at a time is as quick as hundreds, and
        // queueing one work item per file would hand the whole thread pool - which the rest of the
        // app shares - to the scan for as long as it runs.
        return Task.Run<IEnumerable<LocalMod>>(() =>
        {
            var files = Directory.EnumerateFiles(path)
                .Where(IsCandidate)
                .ToList();

            var mods = new LocalMod?[files.Count];

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            // Indexed rather than collected, so the results keep the order of the folder.
            Parallel.For(0, files.Count, options, i => mods[i] = GetModFromFile(files[i], Log, cancellationToken));

            return mods.OfType<LocalMod>().ToList();
        }, cancellationToken);
    }

    /// <summary>Whether this file is even shaped like a mod. See <see cref="_modArchiveExtensions"/>.</summary>
    private static bool IsCandidate(string path)
        => _modArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One candidate archive, or null where it is not a mod.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three outcomes, and only one of them is a fault.</b> A file that is not a mod archive at
    /// all never reaches here - <see cref="IsCandidate"/> filtered it. An archive that opens and
    /// carries no <c>modDesc.xml</c> is a zip that is not a mod, which is a determination rather
    /// than a problem, so it returns null in silence. An archive that will not open, or whose
    /// <c>modDesc</c> will not parse, is the third case: something claiming to be a mod that this
    /// adapter cannot read, and the only one worth telling anybody about.
    /// </para>
    /// <para>
    /// Skipped rather than thrown either way, because a source is any folder the user points at and
    /// one bad archive must not take a thousand good ones down with it.
    /// </para>
    /// </remarks>
    private static LocalMod? GetModFromFile(string path, ILogger log, CancellationToken cancellationToken)
    {
        try
        {
            return ReadModFromFile(path, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or XmlException)
        {
            // A filter rather than a catch body, so a cancelled scan still unwinds.
            //
            // Warning, not Debug: this is a mod archive that could not be read, so in a mod folder it
            // is a mod that has silently left the catalog. Half-written downloads and archives a
            // process still holds open land here too, which is why it does not stop the scan.
            log.LogWarning(ex, "{File} looks like a mod archive but could not be read; it is not in the catalog.", path);

            return null;
        }
    }

    private static LocalMod? ReadModFromFile(string path, CancellationToken cancellationToken)
    {
        using var zip = GetZip(path);

        var maybeDesc = GetModDesc(zip, cancellationToken);
        if (maybeDesc.HasValue is false) return null;

        var desc = maybeDesc.Value;

        // Normalized here, where the id is produced, rather than at each use site - which is how one
        // gets missed. See docs/09-mod-catalog.md#the-casing-trap.
        var maybeLocalMod =
            from filename in Maybe.From(Path.GetFileNameWithoutExtension(path))
            where string.IsNullOrWhiteSpace(filename) is false
            from rawVersion in Maybe.From(desc.Element("version")?.Value)
            where string.IsNullOrWhiteSpace(rawVersion) is false
            from titleGroup in Maybe.From(desc.Element("title"))
            from title in GetEnglishOrFallback(titleGroup, filename)
            from descriptionGroup in Maybe.From(desc.Element("description"))
            from description in GetEnglishOrFallback(descriptionGroup, "")
            select new LocalMod(
                ModKey.From(filename),
                ModVersionKey.From(rawVersion),
                title,
                NormalizeDescription(description),
                () => File.OpenRead(path))
            {
                FilePath = path,
                FileLength = new FileInfo(path).Length,
                Author = desc.Element("author")?.Value.Trim(),
                Locked = DeclaresMaps(desc),
                Icon = GetIcon(zip, path, desc),
                Images = GetImages(zip, path)
            };

        return maybeLocalMod.HasValue ? maybeLocalMod.Value : null;
    }

    private static ZipArchive GetZip(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch
        {
            // 'leaveOpen: false' only starts applying once the archive exists, so a constructor that
            // throws leaves the handle to close - once per non-zip in the folder, which in Downloads
            // is most of them.
            stream.Dispose();
            throw;
        }
    }

    private static Maybe<XElement> GetModDesc(ZipArchive zip, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Reading the entry list is where a damaged central directory surfaces, not the constructor.
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

    /// <summary>
    /// Whether the mod adds a map, which is what makes a Farming Simulator mod version-sensitive:
    /// changing map versions partway through a save can corrupt it, and the damage shows up long
    /// after the change that caused it. Every version of a map mod declares its maps here, so the
    /// answer is the same for all of them without anything having to store it.
    /// </summary>
    private static bool DeclaresMaps(XElement desc)
    {
        return desc.Element("maps")?.Elements("map").Any() ?? false;
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
    private static ModImage? GetIcon(ZipArchive zip, string modPath, XElement desc)
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
    private static IReadOnlyList<ModImage> GetImages(ZipArchive zip, string modPath)
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

    private static ModImage CreateImage(string modPath, ZipArchiveEntry entry)
    {
        // The entry belongs to an archive that is about to be closed - capture the name instead.
        var entryName = entry.FullName;
        var cacheKey = $"{modPath}|{entryName}|{entry.Length}|{entry.Crc32}";

        return new ModImage(entry.Name, cacheKey, async cancellationToken =>
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
        return new FarmingSimulatorInstanceModAdapter(instanceSettings, Loggers);
    }

    public IInstanceModAdapter WithInstanceSettings(DynamicForm instanceSettings)
    {
        if (instanceSettings is not FarmingSimulatorInstanceSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorInstanceSettings>(instanceSettings);
        }
        settings.EnsureValid();
        return new FarmingSimulatorInstanceModAdapter(settings, Loggers);
    }
}


public class FarmingSimulatorInstanceModAdapter(
    FarmingSimulatorInstanceSettings instanceSettings,
    ILoggerFactory? loggerFactory = null)
    : FarmingSimulatorBaseModAdapter(loggerFactory), IInstanceModAdapter
{
    public string ModFolder => Path.Combine(
        instanceSettings.GameDataFolder ?? throw new InvalidOperationException("Instance settings carry no game data folder."),
        "mods");


    public Task<IEnumerable<LocalMod>> GetInstalledMods(CancellationToken cancellationToken)
    {
        return GetModsFromFolder(ModFolder, cancellationToken);
    }

    /// <summary>
    /// One archive per mod, under the name the repo registered for it - which came from the file the
    /// mod was imported from, so the version never appears in it. Installing a different version
    /// replaces the same file.
    /// </summary>
    /// <remarks>
    /// The registered name rather than the mod id, because <see cref="ModKey"/> is lower-cased and
    /// the id is not what the file is called. Building the name from the id renamed every archive in
    /// the folder on the first apply, which is a real difference: Farming Simulator's mod list shows
    /// filenames, and a mod that refers to another by name is reading a string the user can see.
    /// Falls back to the id where the repo has nothing usable registered - an older row, or a name
    /// that failed validation - which is exactly what this used to do for everything.
    /// </remarks>
    public string GetModFilePath(ModKey modId, ModVersionKey versionId, ModFileName? fileName)
    {
        return Path.Combine(ModFolder, fileName?.Value ?? $"{modId.Value}.zip");
    }
}
