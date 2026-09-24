using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using System.Collections.Concurrent;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Turns desired, installed, stored and registered into a list of actions. No I/O beyond hashing the
/// few files whose stat no longer matches the manifest, and nothing here changes anything.
/// </summary>
public static class ModSyncPlanner
{
    /// <summary>
    /// How many files are hashed at once. More than one, because a first sync or a folder the user
    /// filled reads every archive in it, and one at a time leaves the disk's queue and every core but
    /// one idle; not many more, because it is disk bound and the rest of the app shares the pool.
    /// </summary>
    private const int _concurrentHashes = 4;


    /// <param name="registered">
    /// What the repo can reproduce, keyed by content rather than by version id. Recoverability is a
    /// property of the bytes: a file whose hash the repo holds can be fetched again whatever it
    /// calls itself, and a file wearing a registered version id while containing something else
    /// cannot. Only consulted for files that are about to be removed - and, where it carries them,
    /// for the titles every item is named by, ahead of whatever the file or the manifest said.
    /// </param>
    /// <param name="hashFile">
    /// How to read a file's content hash, and where to say how far through the file it has got.
    /// Injected so the planner stays testable, and so the fallback can be exercised for real rather
    /// than mocked. Called for several files at once.
    /// </param>
    /// <param name="progress">
    /// Where to say how planning is getting on, for the strip. Optional, and the count is of mods
    /// examined rather than of files hashed: most of them answer from the manifest without being
    /// opened, and a bar that only moved for the slow ones would stand still through the slowest
    /// stretch there is. Everything the manifest answers is counted at once; a file that does have to
    /// be opened is a row of its own with its bytes, several at a time, the way fetching reports.
    /// </param>
    public static async Task<IReadOnlyList<ModSyncItem>> PlanAsync(
        IReadOnlyCollection<DesiredMod> desired,
        IReadOnlyCollection<InstalledMod> installed,
        RegisteredContent registered,
        SyncManifest? manifest,
        Func<string, CancellationToken, IProgress<long>?, Task<string>>? hashFile,
        CancellationToken cancellationToken,
        IProgress<ModSyncProgress>? progress = null)
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

        // What the two loops below examine between them, counted exactly rather than as an upper
        // bound: a mod that is both wanted and installed is looked at once, by the first loop, and a
        // bar that stopped short of its own total on every ordinary re-apply would be the wrong kind
        // of honest.
        var matched = desired.Count(x => installedByMod.ContainsKey(x.ModId));
        var total = desired.Count + installed.Count - matched;

        // Every installed file is classified on its bytes, so every one needs a hash - and they are
        // all worked out before anything is classified, so the ones that have to be read can be read
        // side by side rather than one after another. A file is named for the mod it is wanted as,
        // where it is wanted, which is the name the rest of the apply calls it by.
        var wantedNames = new Dictionary<ModKey, string>();

        foreach (var want in desired)
        {
            wantedNames.TryAdd(want.ModId, want.DisplayName ?? want.ModId.Value);
        }

        var hashes = await ResolveHashesAsync(
            [
                .. installedByMod.Values.Select(x => (x, wantedNames.GetValueOrDefault(x.ModId) ?? x.DisplayName)),
                .. duplicates.Select(x => (x, x.DisplayName))
            ],
            recorded,
            hashFile,
            total,
            progress,
            cancellationToken);

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
                    DisplayName = registered.NameOf(want.ContentHash) ?? want.DisplayName ?? want.ModId.Value,
                    DesiredVersion = want.VersionId,
                    DesiredHash = want.ContentHash,
                    DesiredSize = want.SizeBytes,
                    FileName = want.FileName,
                    Locked = want.Locked
                });

                continue;
            }

            var hash = hashes[have.Path];

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
                DisplayName = registered.NameOf(want.ContentHash) ?? want.DisplayName ?? have.DisplayName,
                DesiredVersion = want.VersionId,
                DesiredHash = want.ContentHash,
                DesiredSize = want.SizeBytes,
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

            var hash = hashes[have.Path];
            var recoverable = registered.Holds(hash);

            items.Add(new ModSyncItem
            {
                Action = recoverable ? ModSyncAction.UninstallRecoverable : ModSyncAction.Quarantine,
                ModId = have.ModId,
                DisplayName = registered.NameOf(hash) ?? have.DisplayName,
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
    /// Every installed file's hash, by path: the manifest's where the file is still the one it
    /// describes, and a fresh hash otherwise.
    /// </summary>
    /// <remarks>
    /// A file whose size and modification time match the manifest is the file the manifest describes,
    /// so its recorded hash is the answer and no archive is opened. Only a file that fails that check
    /// is read - which on a folder the user populated themselves, or a first sync, is all of them.
    /// That is the honest cost of not knowing, and it is paid a few files at a time.
    /// </remarks>
    private static async Task<IReadOnlyDictionary<string, string?>> ResolveHashesAsync(
        IReadOnlyList<(InstalledMod Have, string Name)> installed,
        IReadOnlyDictionary<string, SyncManifestEntry> recorded,
        Func<string, CancellationToken, IProgress<long>?, Task<string>> hashFile,
        int total,
        IProgress<ModSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var hashes = new ConcurrentDictionary<string, string?>(StringComparer.Ordinal);
        var unknown = new List<(InstalledMod Have, string Name)>();

        foreach (var (have, name) in installed)
        {
            if (recorded.TryGetValue(Path.GetFileName(have.Path), out var entry) &&
                entry.Size == have.Size &&
                entry.ModifiedUtc == have.ModifiedUtc)
            {
                hashes[have.Path] = entry.ContentHash;
            }
            else
            {
                unknown.Add((have, name));
            }
        }

        var run = new HashRun(total - unknown.Count, total, progress);

        run.Begin();

        await Parallel.ForEachAsync(
            unknown,
            new ParallelOptions { MaxDegreeOfParallelism = _concurrentHashes, CancellationToken = cancellationToken },
            async (file, ct) =>
            {
                var (have, name) = file;

                run.Report(name, 0, have.Size);

                hashes[have.Path] = await HashAsync(have.Path, hashFile, run.BytesOf(name, have.Size), ct);

                run.Finish(name);
            });

        run.End();

        return hashes;
    }

    private static async Task<string?> HashAsync(
        string path,
        Func<string, CancellationToken, IProgress<long>?, Task<string>> hashFile,
        IProgress<long>? bytesRead,
        CancellationToken cancellationToken)
    {
        try
        {
            return await hashFile(path, cancellationToken, bytesRead);
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


    /// <summary>
    /// The planning half of the strip, told from several files at once. Serialised, so the count
    /// only ever moves forward and a report never arrives out of the order it was made in.
    /// </summary>
    /// <param name="completed">What the manifest answered, and what is only being installed - done before anything is read.</param>
    private sealed class HashRun(int completed, int total, IProgress<ModSyncProgress>? progress)
    {
        private readonly Lock _gate = new();

        private int _completed = completed;


        /// <summary>Everything that needed no reading, at once, before the first file is opened.</summary>
        public void Begin() => Send(new ModSyncProgress(ModSyncPhase.Planning, _completed, total));

        public void Report(string name, long bytesRead, long size)
            => Send(new ModSyncProgress(ModSyncPhase.Planning, _completed, total)
            {
                Detail = name,
                BytesTransferred = bytesRead,
                TotalBytes = size,
                Concurrent = true
            });

        /// <summary>
        /// Null without a strip, so the hash takes its quicker path that reports nothing.
        /// </summary>
        public IProgress<long>? BytesOf(string name, long size)
            => progress is null ? null : new InlineProgress<long>(read => Report(name, read, size));

        public void Finish(string name)
        {
            lock (_gate)
            {
                _completed++;

                progress?.Report(new ModSyncProgress(ModSyncPhase.Planning, _completed, total)
                {
                    Detail = name,
                    Concurrent = true,
                    ItemFinished = true
                });
            }
        }

        /// <summary>Not concurrent, so whatever rows are left are closed.</summary>
        public void End() => Send(new ModSyncProgress(ModSyncPhase.Planning, _completed, total));

        private void Send(ModSyncProgress value)
        {
            if (progress is null)
            {
                return;
            }

            lock (_gate)
            {
                progress.Report(value with { Completed = _completed });
            }
        }
    }
}
