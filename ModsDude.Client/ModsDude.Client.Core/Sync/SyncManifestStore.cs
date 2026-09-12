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

    /// <summary>
    /// Writes through a temporary file and moves it into place, so an interrupted write leaves the
    /// previous manifest rather than a truncated one.
    /// </summary>
    public void Write(SyncManifest manifest)
    {
        var path = GetPath(manifest.Target);

        lock (_lock)
        {
            Directory.CreateDirectory(_directory);

            var temporaryPath = $"{path}.tmp";

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, _serializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
    }

    /// <summary>Forgets what one folder last had installed - for a target being disconnected.</summary>
    public void Delete(ModTargetRef target)
    {
        lock (_lock)
        {
            try
            {
                File.Delete(GetPath(target));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A manifest for a target that no longer exists is inert.
                _log.LogDebug(exception, "Could not delete the sync manifest for target {Target}.", target);
            }
        }
    }

    /// <summary>
    /// What every one of a game's folders agrees it is running, or null where they do not agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For the savegame side, which is keyed on the game and not yet on a target.</b> A held
    /// savegame's revision is the revision of the folder it was played against, and a binding does
    /// not say which folder that is until slice 3 of Phase 10 puts the target key on it. Every target
    /// of a game follows one profile, so where they all report the same profile and the same revision
    /// there is only one answer and it does not matter which folder the save ran in.
    /// </para>
    /// <para>
    /// Disagreement - including a target that has never been applied to, and a game reaching no
    /// folder at all - is <b>unknown rather than guessed</b>. It means one folder did not get an
    /// apply that another did, and attributing an evening to either number would be recording a mod
    /// list the save may never have run on. Nothing is recorded instead, which is the same answer
    /// this design gives a folder sitting on a different profile.
    /// </para>
    /// </remarks>
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
    private string GetPath(ModTargetRef target)
        => Path.Combine(_directory, $"{StoreFileName.For(target.Game.ToString(), target.Key.Value)}.json");
}
