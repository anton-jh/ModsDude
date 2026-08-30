using ModsDude.Client.Core.Helpers;
using System.Text.Json;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Reads and writes <c>manifests/{instanceId}.json</c>, one file per instance beside
/// <c>state.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Not inline in <see cref="Persistence.LocalState"/>, which is loaded eagerly and rewritten
/// whenever any instance changes: a manifest for 2,000 mods with a hash each is a few hundred
/// kilobytes with no business being re-serialised because somebody renamed something. And not in the
/// game's own folder, which an in-game updater rewrites.
/// </para>
/// <para>
/// Written <b>only on success, atomically</b>. A sync that fails halfway leaves the previous
/// manifest, so the next check reports drift - which is true, and re-applying fixes it. A partly
/// written one would instead claim a state that never existed.
/// </para>
/// </remarks>
public sealed class SyncManifestStore
{
    private readonly static JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly Lock _lock = new();


    public SyncManifestStore()
        : this(Path.Combine(FileSystemHelper.GetAppDataDirectory(), "manifests"))
    { }

    /// <param name="directory">Where the manifests live. Named so tests can point it somewhere else.</param>
    public SyncManifestStore(string directory)
    {
        _directory = directory;
    }


    /// <returns>
    /// Null when there is none, when it cannot be read, or when it was written by an incompatible
    /// version. All three mean the same thing to a caller - fall back to a full reconcile.
    /// </returns>
    public SyncManifest? TryRead(Guid instanceId)
    {
        var path = GetPath(instanceId);

        lock (_lock)
        {
            try
            {
                if (File.Exists(path) is false)
                {
                    return null;
                }

                var manifest = JsonSerializer.Deserialize<SyncManifest>(File.ReadAllText(path));

                return manifest?.Version == SyncManifest.CurrentVersion ? manifest : null;
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Writes through a temporary file and moves it into place, so an interrupted write leaves the
    /// previous manifest rather than a truncated one.
    /// </summary>
    public void Write(SyncManifest manifest)
    {
        var path = GetPath(manifest.InstanceId);

        lock (_lock)
        {
            Directory.CreateDirectory(_directory);

            var temporaryPath = $"{path}.tmp";

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, _serializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
    }

    /// <summary>Forgets what an instance last installed - for an instance being deleted.</summary>
    public void Delete(Guid instanceId)
    {
        lock (_lock)
        {
            try
            {
                File.Delete(GetPath(instanceId));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A manifest for an instance that no longer exists is inert.
            }
        }
    }


    private string GetPath(Guid instanceId) => Path.Combine(_directory, $"{instanceId}.json");
}
