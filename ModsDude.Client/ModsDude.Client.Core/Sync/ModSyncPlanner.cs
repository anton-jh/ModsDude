using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Turns desired, installed, stored and registered into a list of actions. No I/O beyond hashing the
/// few files whose stat no longer matches the manifest, and nothing here changes anything.
/// </summary>
public static class ModSyncPlanner
{
    /// <param name="registered">
    /// What the repo can reproduce, keyed by content rather than by version id. Recoverability is a
    /// property of the bytes: a file whose hash the repo holds can be fetched again whatever it
    /// calls itself, and a file wearing a registered version id while containing something else
    /// cannot. Only consulted for files that are about to be removed.
    /// </param>
    /// <param name="hashFile">
    /// How to read a file's content hash. Injected so the planner stays testable, and so the fallback
    /// can be exercised for real rather than mocked.
    /// </param>
    public static async Task<IReadOnlyList<ModSyncItem>> PlanAsync(
        IReadOnlyCollection<DesiredMod> desired,
        IReadOnlyCollection<InstalledMod> installed,
        RegisteredContent registered,
        SyncManifest? manifest,
        Func<string, CancellationToken, Task<string>>? hashFile,
        CancellationToken cancellationToken)
    {
        hashFile ??= ContentStore.HashFileAsync;

        var recorded = BuildRecordedHashes(manifest);
        var installedByMod = new Dictionary<ModKey, InstalledMod>();
        var duplicates = new List<InstalledMod>();

        foreach (var mod in installed)
        {
            // One mod folder should hold one file per mod, but nothing enforces it - two archives
            // whose names differ only in case, or an adapter that finds the same mod twice. The
            // first is the installation; the rest are files nobody asked for and are treated as
            // such, which means the Recycle Bin rather than a delete.
            if (installedByMod.TryAdd(mod.ModId, mod) is false)
            {
                duplicates.Add(mod);
            }
        }

        var items = new List<ModSyncItem>();

        foreach (var want in desired)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (installedByMod.Remove(want.ModId, out var have) is false)
            {
                items.Add(new ModSyncItem
                {
                    Action = ModSyncAction.Install,
                    ModId = want.ModId,
                    DisplayName = want.DisplayName ?? want.ModId.Value,
                    DesiredVersion = want.VersionId,
                    DesiredHash = want.ContentHash,
                    FileName = want.FileName,
                    Locked = want.Locked
                });

                continue;
            }

            var hash = await ResolveHashAsync(have, recorded, hashFile, cancellationToken);

            // Compared on bytes, not on version id. GetInstalledMods reads the version out of the
            // mod's own metadata, so two different builds both calling themselves 1.0.0 are
            // indistinguishable to it - and that happens in practice. Without this, content
            // addressing protects the store and does nothing for the mod folder.
            // See docs/09-mod-catalog.md#same-mod-several-sources.
            var matches = ModContentHasher.Matches(hash, want.ContentHash);

            items.Add(new ModSyncItem
            {
                Action = matches
                    ? (IsMisnamed(have.Path, want.FileName) ? ModSyncAction.Rename : ModSyncAction.Keep)
                    : ModSyncAction.Replace,
                ModId = want.ModId,
                DisplayName = want.DisplayName ?? have.DisplayName,
                DesiredVersion = want.VersionId,
                DesiredHash = want.ContentHash,
                FileName = want.FileName,
                Locked = want.Locked,
                InstalledVersion = have.VersionId,
                InstalledPath = have.Path,
                InstalledHash = hash,
                InstalledSize = have.Size,
                InstalledIsRecoverable = registered.Holds(hash)
            });
        }

        foreach (var have in installedByMod.Values.Concat(duplicates))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash = await ResolveHashAsync(have, recorded, hashFile, cancellationToken);
            var recoverable = registered.Holds(hash);

            items.Add(new ModSyncItem
            {
                Action = recoverable ? ModSyncAction.UninstallRecoverable : ModSyncAction.Quarantine,
                ModId = have.ModId,
                DisplayName = have.DisplayName,
                InstalledVersion = have.VersionId,
                InstalledPath = have.Path,
                InstalledHash = hash,
                InstalledSize = have.Size,
                InstalledIsRecoverable = recoverable
            });
        }

        return items;
    }


    /// <summary>
    /// Whether the right file is under the wrong name - compared ordinally, since case is the whole
    /// point.
    /// </summary>
    /// <remarks>
    /// Only asked of a file whose bytes already match, so it can never be confused with a mod
    /// changing what it calls itself between versions: that arrives as different content and is a
    /// replace. A repo with nothing usable registered has no opinion, and nothing is renamed.
    /// </remarks>
    private static bool IsMisnamed(string path, ModFileName? wanted)
    {
        return wanted is ModFileName name
            && string.Equals(Path.GetFileName(path), name.Value, StringComparison.Ordinal) is false;
    }

    /// <summary>
    /// The manifest's hash where the file is still the one it describes, and a fresh hash otherwise.
    /// </summary>
    /// <remarks>
    /// A file whose size and modification time match the manifest is the file the manifest describes,
    /// so its recorded hash is the answer and no archive is opened. Only a file that fails that check
    /// is read - which on a folder the user populated themselves, or a first sync, is all of them.
    /// That is the honest cost of not knowing.
    /// </remarks>
    private static async Task<string?> ResolveHashAsync(
        InstalledMod installed,
        IReadOnlyDictionary<string, SyncManifestEntry> recorded,
        Func<string, CancellationToken, Task<string>> hashFile,
        CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(installed.Path);

        if (recorded.TryGetValue(name, out var entry) &&
            entry.Size == installed.Size &&
            entry.ModifiedUtc == installed.ModifiedUtc)
        {
            return entry.ContentHash;
        }

        try
        {
            return await hashFile(installed.Path, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // A file that cannot be read - locked by the running game, or gone since the scan -
            // has an unknown hash, which classification treats as "not the wanted bytes" and as
            // "not recoverable". Both are the cautious answer.
            return null;
        }
    }

    private static IReadOnlyDictionary<string, SyncManifestEntry> BuildRecordedHashes(SyncManifest? manifest)
    {
        var recorded = new Dictionary<string, SyncManifestEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in manifest?.Entries ?? [])
        {
            recorded[entry.FileName] = entry;
        }

        return recorded;
    }
}
