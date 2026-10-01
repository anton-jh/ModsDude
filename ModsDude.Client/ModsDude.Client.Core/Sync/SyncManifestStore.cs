using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using System.Text.Json;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Reads and writes <c>manifests/{game-identity}_{target-key}.json</c>, one file per target beside
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
/// <b>One file per target, not per game.</b> Syncing a dedicated server must not rewrite the record
/// of what the MP client is running, and the write below is atomic per file - so a run that reaches
/// one folder and fails at the next leaves two manifests each describing its own folder truthfully.
/// </para>
/// <para>
/// Written <b>only on success, atomically</b>. A sync that fails halfway leaves the previous
/// manifest, so the next check reports drift - which is true, and re-applying fixes it. A partly
/// written one would instead claim a state that never existed.
/// </para>
/// </remarks>
public sealed class SyncManifestStore : ISyncManifestStore
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


    public SyncManifest? TryRead(ModTargetRef target)
    {
        var path = GetPath(target);

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
                _log.LogWarning(exception, "Could not read the sync manifest for target {Target}.", target);

                return null;
            }
        }
    }

    public void Write(SyncManifest manifest)
    {
        var path = GetPath(manifest.Target);

        lock (_lock)
        {
            Directory.CreateDirectory(_directory);

            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(manifest, _serializerOptions));
        }
    }

    public void DropStale(IEnumerable<ModTargetRef> expected)
    {
        var keep = new HashSet<string>(expected.Select(FileName), StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            if (Directory.Exists(_directory) is false)
            {
                return;
            }

            IReadOnlyList<string> present;

            try
            {
                present = [.. Directory.EnumerateFiles(_directory)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _log.LogDebug(exception, "Could not list the manifest directory to sweep it.");

                return;
            }

            foreach (var path in present)
            {
                // Every file rather than *.json, which collects the .tmp an interrupted atomic write
                // leaves behind as well.
                if (keep.Contains(Path.GetFileName(path)))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);

                    _log.LogInformation("Dropped the stale sync manifest {File}.", Path.GetFileName(path));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A manifest nothing will read is inert wherever it sits.
                    _log.LogDebug(exception, "Could not drop the stale sync manifest {File}.", Path.GetFileName(path));
                }
            }
        }
    }

    public SyncManifest? TryReadAgreed(IEnumerable<ModTargetRef> targets)
    {
        SyncManifest? agreed = null;

        foreach (var target in targets)
        {
            if (TryRead(target) is not SyncManifest manifest)
            {
                return null;
            }

            if (agreed is null)
            {
                agreed = manifest;

                continue;
            }

            if (agreed.ProfileId != manifest.ProfileId || agreed.ProfileRevision != manifest.ProfileRevision)
            {
                return null;
            }
        }

        return agreed;
    }


    /// <summary>
    /// Through <see cref="StoreFileName"/>, which is where what a store puts in a file name gets
    /// encoded. Both parts are adapter-authored - the identity's discriminator, which a scripted
    /// adapter declares from inside its script, and the target key - so the encoding has to be the
    /// store's rather than something an adapter can violate. Ordinary ones stay legible:
    /// <c>_farming_simulator#fs25_mods.json</c>.
    /// </summary>
    private string GetPath(ModTargetRef target) => Path.Combine(_directory, FileName(target));

    /// <inheritdoc cref="GetPath"/>
    /// <remarks>
    /// Named separately because <see cref="DropStale"/> builds the name it would have written for
    /// every target it still expects and compares directory entries against those. Nothing ever
    /// reads a name back into its parts - see <see cref="StoreFileName"/>, which is deliberately not
    /// reversible - so the two have to come from one place or the sweep would drop live manifests
    /// the moment the encoding changed.
    /// </remarks>
    private static string FileName(ModTargetRef target)
        => $"{StoreFileName.For(target.Game.ToString(), target.Key.Value)}.json";
}
