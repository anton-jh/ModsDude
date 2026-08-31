using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

public enum InstanceDriftStatus
{
    /// <summary>The folder still matches what the last sync installed, and the profile still pins it.</summary>
    InSync,

    /// <summary>Mods were added, removed or replaced since the last sync - or the profile changed underneath it.</summary>
    Drifted,

    /// <summary>Nothing has been applied to this instance yet.</summary>
    NoActiveProfile,

    /// <summary>The active profile was deleted, or the user was removed from its repo.</summary>
    DanglingProfile,

    /// <summary>
    /// An active profile with no manifest - a fresh install, or discarded local state. Drift is
    /// simply not known, and a full reconcile produces the right answer anyway.
    /// </summary>
    NeverSynced,

    /// <summary>
    /// An unplugged drive or an offline network path. Unknown, <b>not</b> drifted: warning about mods
    /// that may be perfectly fine is worse than saying nothing.
    /// </summary>
    FolderUnreachable
}

/// <summary>Why a locked mod is being named. The wording differs because the remedy does.</summary>
public enum LockedDriftReason
{
    /// <summary>Its file is not the one that was installed - an in-game update-all looks like this.</summary>
    FileChanged,

    /// <summary>Its file is gone from the mod folder entirely.</summary>
    FileRemoved,

    /// <summary>The profile now pins a different version than the one applied here.</summary>
    ProfileMoved
}

/// <param name="AppliedVersion">What the last sync put there, which is the version the save was built against.</param>
public sealed record DriftedLockedMod(ModKey ModId, string DisplayName, string? AppliedVersion, LockedDriftReason Reason);

/// <param name="Added">Names in the folder that the last sync did not put there.</param>
/// <param name="Removed">Names the last sync installed that are no longer in the folder.</param>
/// <param name="Changed">Names whose size or modification time no longer match - a mod was replaced or updated.</param>
/// <param name="ProfileChangedMods">
/// Mods the profile pins differently from what was applied. This is what catches somebody else
/// having edited the shared profile since this instance synced, without needing a revision number on
/// the profile - which it does not have.
/// </param>
public sealed record InstanceDriftReport(
    InstanceDriftStatus Status,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed,
    IReadOnlyList<ModKey> ProfileChangedMods)
{
    public static InstanceDriftReport For(InstanceDriftStatus status) => new(status, [], [], [], []);

    public int DifferenceCount => Added.Count + Removed.Count + Changed.Count + ProfileChangedMods.Count;

    /// <summary>
    /// The locked mods among the differences, named. An unlocked mod at the wrong version is untidy;
    /// a locked map at the wrong version is a damaged savegame waiting to happen, so it is named -
    /// with the consequence - rather than folded into a count.
    /// </summary>
    public IReadOnlyList<DriftedLockedMod> LockedDrift { get; init; } = [];

    public IReadOnlyList<ModKey> LockedMods => [.. LockedDrift.Select(x => x.ModId)];

    public bool HasLockedDrift => LockedDrift.Count > 0;
}


/// <summary>
/// Whether an instance's mod folder still matches what was applied to it, answered without opening a
/// single archive.
/// </summary>
/// <remarks>
/// <para>
/// The expensive part of reconciliation is reading archives, not listing a directory: a single
/// non-recursive enumeration over 2,000 entries taking name, size and modification time is
/// milliseconds. So the cheap check is that listing compared against the manifest, which catches all
/// three cases that matter - added, removed, and replaced.
/// </para>
/// <para>
/// This detects and exposes drift. Surfacing it in the shell - the app-level notice, save-and-apply,
/// activation - is Phase 4's, see docs/PLAN.md#phase-4--make-drift-unmissable.
/// </para>
/// </remarks>
public sealed class InstanceDriftService(SyncManifestStore manifestStore)
{
    /// <param name="activeProfile">
    /// The instance's standing intent. Passed rather than read off the instance so this depends on
    /// the two facts it actually uses and nothing else.
    /// </param>
    /// <param name="modFolder">
    /// Null where the instance's adapter cannot be hydrated - an instance whose scope no repo on this
    /// machine serves. Unknown rather than drifted, like any other unreachable folder.
    /// </param>
    /// <param name="profileIsMissing">
    /// Whether the repo says the active profile is gone. The caller knows; this cannot ask.
    /// </param>
    /// <param name="profileDependencies">
    /// What the profile pins right now, where the caller already had it. Null skips the
    /// profile-changed comparison and leaves the folder check to stand on its own.
    /// </param>
    public InstanceDriftReport Check(
        Guid instanceId,
        ActiveProfile? activeProfile,
        string? modFolder,
        bool profileIsMissing = false,
        IReadOnlyCollection<DesiredMod>? profileDependencies = null)
    {
        if (activeProfile is not ActiveProfile active)
        {
            return InstanceDriftReport.For(InstanceDriftStatus.NoActiveProfile);
        }

        if (profileIsMissing)
        {
            return InstanceDriftReport.For(InstanceDriftStatus.DanglingProfile);
        }

        if (modFolder is null || Directory.Exists(modFolder) is false)
        {
            return InstanceDriftReport.For(InstanceDriftStatus.FolderUnreachable);
        }

        var manifest = manifestStore.TryRead(instanceId);

        // A manifest describing another profile, or another folder, says nothing about this one -
        // the same position as having none, which is a full reconcile rather than a false alarm.
        if (manifest is null ||
            manifest.ProfileId != active.ProfileId ||
            manifest.RepoId != active.RepoId ||
            FileSystemHelper.ArePathsEqual(manifest.ModFolder, modFolder) is false)
        {
            return InstanceDriftReport.For(InstanceDriftStatus.NeverSynced);
        }

        List<string> listing;

        try
        {
            listing = [.. Directory.EnumerateFiles(modFolder).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception)
        {
            return InstanceDriftReport.For(InstanceDriftStatus.FolderUnreachable);
        }

        var (added, removed, changed) = CompareFolder(manifest, listing, modFolder);
        var (profileChanged, locked) = CompareProfile(manifest, profileDependencies);

        var status = added.Count + removed.Count + changed.Count + profileChanged.Count > 0
            ? InstanceDriftStatus.Drifted
            : InstanceDriftStatus.InSync;

        return new InstanceDriftReport(status, added, removed, changed, profileChanged)
        {
            // One entry per mod. A locked map whose file the game replaced and whose pin somebody
            // then moved is one problem, and the file is the half that is already on disk.
            LockedDrift = [.. NameLockedFiles(manifest, removed, changed).Concat(locked).DistinctBy(x => x.ModId)]
        };
    }


    /// <summary>
    /// The locked mods behind the changed and removed file names. The manifest carries the lock, so
    /// this needs neither the profile's current dependencies nor a single archive opened - which is
    /// what lets the startup check say "your map moved" rather than "3 files differ".
    /// </summary>
    private static List<DriftedLockedMod> NameLockedFiles(
        SyncManifest manifest,
        IReadOnlyList<string> removed,
        IReadOnlyList<string> changed)
    {
        var byName = manifest.Entries
            .Where(x => x.Locked)
            .ToDictionary(x => x.FileName, StringComparer.OrdinalIgnoreCase);

        if (byName.Count == 0)
        {
            return [];
        }

        var result = new List<DriftedLockedMod>();

        foreach (var (names, reason) in new[]
        {
            (changed, LockedDriftReason.FileChanged),
            (removed, LockedDriftReason.FileRemoved)
        })
        {
            foreach (var name in names)
            {
                if (byName.TryGetValue(name, out var entry))
                {
                    result.Add(new DriftedLockedMod(
                        ModKey.From(entry.ModId),
                        entry.DisplayName ?? entry.ModId,
                        entry.VersionId,
                        reason));
                }
            }
        }

        return result;
    }


    private static (List<string> Added, List<string> Removed, List<string> Changed) CompareFolder(
        SyncManifest manifest,
        IReadOnlyList<string> listing,
        string modFolder)
    {
        var byName = manifest.Entries.ToDictionary(x => x.FileName, StringComparer.OrdinalIgnoreCase);
        var present = new HashSet<string>(listing, StringComparer.OrdinalIgnoreCase);
        var unmanaged = new HashSet<string>(manifest.UnmanagedFileNames, StringComparer.OrdinalIgnoreCase);

        var added = new List<string>();
        var changed = new List<string>();

        foreach (var name in listing)
        {
            if (byName.TryGetValue(name, out var entry) is false)
            {
                // A file sync never installed and was already ignoring is not an addition. One that
                // was not there at the last sync is, whatever it turns out to be.
                if (unmanaged.Contains(name) is false)
                {
                    added.Add(name);
                }

                continue;
            }

            var info = new FileInfo(Path.Combine(modFolder, name));

            // The recorded hashes are not read here at all. Size and time are what a listing gives
            // for free, and only a file that fails them is worth opening.
            if (info.Length != entry.Size || info.LastWriteTimeUtc != entry.ModifiedUtc)
            {
                changed.Add(name);
            }
        }

        return (added, [.. byName.Keys.Where(x => present.Contains(x) is false)], changed);
    }

    /// <summary>
    /// The mod set that was applied against what the profile pins now. Any difference means somebody
    /// edited the shared profile since this instance synced.
    /// </summary>
    private static (List<ModKey> Changed, List<DriftedLockedMod> Locked) CompareProfile(
        SyncManifest manifest,
        IReadOnlyCollection<DesiredMod>? dependencies)
    {
        if (dependencies is null)
        {
            return ([], []);
        }

        var applied = manifest.Entries.ToDictionary(x => ModKey.From(x.ModId));
        var changed = new List<ModKey>();
        var locked = new List<DriftedLockedMod>();

        foreach (var dependency in dependencies)
        {
            if (applied.Remove(dependency.ModId, out var entry) is false ||
                ModContentHasher.Matches(entry.ContentHash, dependency.ContentHash) is false)
            {
                changed.Add(dependency.ModId);

                if (dependency.Locked)
                {
                    locked.Add(new DriftedLockedMod(
                        dependency.ModId,
                        dependency.DisplayName ?? entry?.DisplayName ?? dependency.ModId.Value,
                        entry?.VersionId,
                        LockedDriftReason.ProfileMoved));
                }
            }
        }

        // Whatever is left was applied and is no longer pinned - the profile lost a mod.
        changed.AddRange(applied.Keys);

        return (changed, locked);
    }
}
