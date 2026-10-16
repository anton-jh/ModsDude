using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using System.Text.Json;

namespace ModsDude.Client.Core.RemoteSources;

/// <summary>
/// The notes, kept in <c>remote-downloads.json</c> beside the other state files. Only the most recently
/// written <see cref="Capacity"/> are kept.
/// </summary>
public sealed class RemoteDownloadNotes : IRemoteDownloadNotes
{
    public const int Capacity = 1000;

    private static readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger _log;
    private readonly Lock _lock = new();

    /// <summary>Oldest first. Null until first read.</summary>
    private List<StoredNote>? _notes;


    public RemoteDownloadNotes(ILogger<RemoteDownloadNotes> logger)
        : this(Path.Combine(FileSystemHelper.GetAppDataDirectory(), "remote-downloads.json"), logger)
    { }

    /// <param name="path">Where the notes live. Named so tests can point it somewhere else.</param>
    public RemoteDownloadNotes(string path, ILogger? logger = null)
    {
        _path = path;
        _log = logger ?? NullLogger.Instance;
    }


    public RemoteDownloadNote? Find(Uri url)
    {
        lock (_lock)
        {
            var key = url.AbsoluteUri;

            return Load().LastOrDefault(x => x.Url == key) is StoredNote stored ? stored.ToNote() : null;
        }
    }

    public void Record(RemoteDownloadNote note)
    {
        lock (_lock)
        {
            var notes = Load();
            var stored = StoredNote.From(note);

            notes.RemoveAll(x => x.Url == stored.Url);
            notes.Add(stored);

            if (notes.Count > Capacity)
            {
                notes.RemoveRange(0, notes.Count - Capacity);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(notes, _serializerOptions));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Kept in memory for this session either way. Losing it costs a second download of a
                // mismatched file later, and the same question asked again.
                _log.LogWarning(exception, "Could not write the remote download notes to {Path}.", _path);
            }
        }
    }


    private List<StoredNote> Load()
    {
        if (_notes is not null)
        {
            return _notes;
        }

        try
        {
            var stored = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<StoredNote>>(File.ReadAllText(_path)) ?? []
                : [];

            _notes = [.. stored.Where(x => x.IsReadable)];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(exception, "Could not read the remote download notes at {Path}; starting empty.", _path);
            _notes = [];
        }

        return _notes;
    }


    private sealed record StoredNote(string Url, string OfferedMod, string OfferedVersion, string? HeldMod, string? HeldVersion)
    {
        public static StoredNote From(RemoteDownloadNote note) => new(
            note.Url.AbsoluteUri,
            note.Offered.ModId.Value,
            note.Offered.VersionId.Value,
            note.Held?.ModId.Value,
            note.Held?.VersionId.Value);

        public bool IsReadable
            => Uri.TryCreate(Url, UriKind.Absolute, out _)
            && string.IsNullOrWhiteSpace(OfferedMod) is false
            && string.IsNullOrWhiteSpace(OfferedVersion) is false
            && (HeldMod is null) == (HeldVersion is null)
            && (HeldMod is null || (string.IsNullOrWhiteSpace(HeldMod) is false && string.IsNullOrWhiteSpace(HeldVersion) is false));

        public RemoteDownloadNote ToNote() => new(
            new Uri(Url),
            new ModVersionIdentity(ModKey.From(OfferedMod), ModVersionKey.From(OfferedVersion)),
            HeldMod is not null && HeldVersion is not null
                ? new ModVersionIdentity(ModKey.From(HeldMod), ModVersionKey.From(HeldVersion))
                : null);
    }
}
