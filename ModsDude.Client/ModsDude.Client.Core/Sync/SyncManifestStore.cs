using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using System.Text.Json;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Reads and writes <c>manifests/{game-identity}.json</c>, one file per game beside
/// <c>state.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Not inline in <see cref="Persistence.LocalState"/>, which is loaded eagerly and rewritten
/// whenever any game changes: a manifest for 2,000 mods with a hash each is a few hundred
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
    private readonly ILogger _log;


    public SyncManifestStore(ILogger<SyncManifestStore> logger)
        : this(Path.Combine(FileSystemHelper.GetAppDataDirectory(), "manifests"), logger)
    { }

    /// <param name="directory">Where the manifests live. Named so tests can point it somewhere else.</param>
    public SyncManifestStore(string directory, ILogger? logger = null)
    {
        _directory = directory;
        _log = logger ?? NullLogger.Instance;
    }


    /// <returns>
    /// Null when there is none, when it cannot be read, or when it was written by an incompatible
    /// version. All three mean the same thing to a caller - fall back to a full reconcile.
    /// </returns>
    public SyncManifest? TryRead(GameIdentity game)
    {
        var path = GetPath(game);

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
                // A manifest that cannot be read costs a full reconcile rather than a delta, which
                // is slow but correct - and invisible, which is why it is written down.
                _log.LogWarning(exception, "Could not read the sync manifest for game {Game}.", game);

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
        var path = GetPath(manifest.Game);

        lock (_lock)
        {
            Directory.CreateDirectory(_directory);

            var temporaryPath = $"{path}.tmp";

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, _serializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
    }

    /// <summary>Forgets what a game last installed - for a game being deleted.</summary>
    public void Delete(GameIdentity game)
    {
        lock (_lock)
        {
            try
            {
                File.Delete(GetPath(game));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A manifest for a game that no longer exists is inert.
                _log.LogDebug(exception, "Could not delete the sync manifest for game {Game}.", game);
            }
        }
    }


    /// <summary>
    /// Through <see cref="StoreFileName"/>, which is where what a store puts in a file name gets
    /// encoded. The identity carries an adapter-authored discriminator, and the target key that joins
    /// it in slice 2b is adapter-authored too, so the encoding has to be the store's rather than
    /// something an adapter can violate. Ordinary ones stay legible:
    /// <c>_farming_simulator#fs25.json</c>.
    /// </summary>
    private string GetPath(GameIdentity game) => Path.Combine(_directory, $"{StoreFileName.For(game.ToString())}.json");
}
