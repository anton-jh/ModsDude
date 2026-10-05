using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Models;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>
/// One BeamNG.drive mod archive, read into what the catalog shows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two kinds of mod, one reader.</b> A mod from the game's repository carries
/// <c>mod_info/&lt;id&gt;/info.json</c> with its title, version, author and description, and an icon
/// and screenshots beside it. A mod somebody packed by hand carries none of that and is still a mod:
/// the file name names it, and the newest file inside it stands in for a version.
/// </para>
/// <para>
/// <b>What makes a zip a mod</b> is holding one of the folders the game loads content from. Any
/// other zip - in Downloads most of them - is not a mod, which is a determination rather than a
/// fault, and is skipped in silence.
/// </para>
/// </remarks>
internal static class BeamNgModArchive
{
    private const string _modInfoFolder = "mod_info";
    private const string _infoFile = "info.json";

    private static readonly HashSet<string> _contentFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        _modInfoFolder, "vehicles", "levels", "art", "ui", "lua", "scripts", "gameplay", "settings", "core"
    };

    private static readonly string[] _imageExtensions = [".jpg", ".jpeg", ".png"];


    /// <param name="fileName">The name the mod goes by, which may differ from the file's own - see <see cref="BeamMpCacheName"/>.</param>
    /// <returns>Null where the archive is not a mod, or could not be read - the second is logged.</returns>
    public static LocalMod? Read(string path, string fileName, ILogger log, CancellationToken cancellationToken)
    {
        try
        {
            return ReadArchive(path, fileName, log, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // A filter rather than a catch body, so a cancelled scan still unwinds.
            //
            // Warning, not Debug: this is a mod archive that could not be read, so in a mod folder it
            // is a mod that has silently left the catalog. Half-written downloads and archives a
            // process still holds open land here too, which is why it does not stop the scan.
            log.LogWarning(exception, "{File} looks like a mod archive but could not be read; it is not in the catalog.", path);

            return null;
        }
    }


    private static LocalMod? ReadArchive(string path, string fileName, ILogger log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stem = Path.GetFileNameWithoutExtension(fileName);

        if (string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        using var zip = ModArchives.Open(path);

        // Reading the entry list is where a damaged central directory surfaces, not the constructor.
        var entries = zip.Entries
            .Where(x => x.Name.Length > 0)
            .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var folders = entries
            .Select(x => TopFolder(x.FullName))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (folders.Overlaps(_contentFolders) is false)
        {
            return null;
        }

        var infoEntry = entries.FirstOrDefault(x => IsModInfoFile(x.FullName));
        var info = infoEntry is null ? null : ReadInfo(infoEntry, path, log);
        var infoFolder = infoEntry is null ? null : Normalize(infoEntry.FullName)[..^_infoFile.Length];

        return new LocalMod(
            ModKey.From(stem),
            ModVersionKey.From(NonBlank(info?.Version) ?? NewestEntryVersion(entries)),
            NonBlank(info?.Title) ?? stem,
            NonBlank(BeamNgBbCode.ToPlainText(info?.Message)) ?? NonBlank(info?.TagLine) ?? "",
            () => File.OpenRead(path))
        {
            FilePath = path,
            FileLength = new FileInfo(path).Length,
            Author = NonBlank(info?.Author),
            Icon = FindIcon(entries, infoFolder, path),
            Images = FindImages(entries, infoFolder, path),
            Attributes = BeamNgModAttributes.Read(folders)
        };
    }

    /// <summary>
    /// The newest file in the archive as a version, <c>yyyy.MM.dd.HHmm</c>: the same for every copy
    /// of the file, and later for a later build, which is all a version has to be.
    /// </summary>
    /// <remarks>
    /// The time is the one the zip stores, as it stores it. Converting it to anything would make it
    /// depend on the time zone of whichever machine read it.
    /// </remarks>
    private static string NewestEntryVersion(IReadOnlyList<ZipArchiveEntry> entries)
    {
        return entries
            .Max(x => x.LastWriteTime.DateTime)
            .ToString("yyyy.MM.dd.HHmm", CultureInfo.InvariantCulture);
    }

    private static ModInfo? ReadInfo(ZipArchiveEntry entry, string path, ILogger log)
    {
        try
        {
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            return new ModInfo(
                ReadString(document.RootElement, "title"),
                ReadString(document.RootElement, "version_string"),
                ReadString(document.RootElement, "username"),
                ReadString(document.RootElement, "tag_line"),
                ReadString(document.RootElement, "message"));
        }
        catch (JsonException exception)
        {
            // The mod is still a mod without its repository page; it just reads as one packed by hand.
            log.LogWarning(exception, "{File} carries an info.json that could not be read; reading the mod without it.", path);

            return null;
        }
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// The repository's icon, or else the picture the game itself shows for the first vehicle or
    /// level in the archive.
    /// </summary>
    private static ModImage? FindIcon(IReadOnlyList<ZipArchiveEntry> entries, string? infoFolder, string path)
    {
        var entry = (infoFolder is null ? null : entries.FirstOrDefault(x => IsIcon(x, infoFolder)))
            ?? entries.FirstOrDefault(IsVehiclePreview)
            ?? entries.FirstOrDefault(IsLevelPreview);

        return entry is null ? null : ModArchives.Image(path, entry);
    }

    /// <summary>The repository's screenshots, or else the previews of the levels the archive adds.</summary>
    private static IReadOnlyList<ModImage> FindImages(IReadOnlyList<ZipArchiveEntry> entries, string? infoFolder, string path)
    {
        var screenshots = infoFolder is null
            ? []
            : entries.Where(x => IsImage(x) && Normalize(x.FullName).StartsWith(infoFolder + "images/", StringComparison.OrdinalIgnoreCase)).ToList();

        var chosen = screenshots.Count > 0
            ? screenshots
            : entries.Where(IsLevelPreview).ToList();

        return [.. chosen.Select(x => ModArchives.Image(path, x))];
    }

    /// <summary><c>mod_info/&lt;id&gt;/info.json</c>, exactly that deep.</summary>
    private static bool IsModInfoFile(string fullName)
    {
        var parts = Normalize(fullName).Split('/');

        return parts.Length == 3
            && string.Equals(parts[0], _modInfoFolder, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], _infoFile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>icon.jpg</c> directly in the repository's folder for the mod.</summary>
    private static bool IsIcon(ZipArchiveEntry entry, string infoFolder)
    {
        var fullName = Normalize(entry.FullName);

        return IsImage(entry)
            && fullName.StartsWith(infoFolder, StringComparison.OrdinalIgnoreCase)
            && fullName.IndexOf('/', infoFolder.Length) < 0
            && string.Equals(Path.GetFileNameWithoutExtension(entry.Name), "icon", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>vehicles/&lt;name&gt;/default.png</c>, the picture the vehicle selector shows.</summary>
    private static bool IsVehiclePreview(ZipArchiveEntry entry)
    {
        var parts = Normalize(entry.FullName).Split('/');

        return parts.Length == 3
            && string.Equals(parts[0], "vehicles", StringComparison.OrdinalIgnoreCase)
            && IsImage(entry)
            && string.Equals(Path.GetFileNameWithoutExtension(parts[2]), "default", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>levels/&lt;name&gt;/*preview*.jpg</c>, the picture the level selector shows.</summary>
    private static bool IsLevelPreview(ZipArchiveEntry entry)
    {
        var parts = Normalize(entry.FullName).Split('/');

        return parts.Length == 3
            && string.Equals(parts[0], "levels", StringComparison.OrdinalIgnoreCase)
            && IsImage(entry)
            && parts[2].Contains("preview", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImage(ZipArchiveEntry entry)
        => _imageExtensions.Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase);

    private static string? TopFolder(string fullName)
    {
        var normalized = Normalize(fullName);
        var slash = normalized.IndexOf('/');

        return slash > 0 ? normalized[..slash] : null;
    }

    private static string Normalize(string fullName) => fullName.Replace('\\', '/').TrimStart('/');

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();


    private sealed record ModInfo(string? Title, string? Version, string? Author, string? TagLine, string? Message);
}
