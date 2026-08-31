using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

/// <summary>One mod the profile pins: the whole of what sync is trying to achieve, per mod.</summary>
/// <param name="ContentHash">
/// What the file must contain. The profile's dependencies carry it, so no sync has to pull the
/// repo's full mod list to find out.
/// </param>
public sealed record DesiredMod(ModKey ModId, ModVersionKey VersionId, string ContentHash, bool Locked)
{
    /// <summary>For the plan preview. Falls back to the mod id, which is what the dependency carries.</summary>
    public string? DisplayName { get; init; }
}

/// <summary>One mod file the adapter found in the mod folder.</summary>
/// <param name="ModifiedUtc">
/// With <paramref name="Size"/>, what says whether the manifest's recorded hash still describes this
/// file - the check that keeps classification from opening 2,000 archives.
/// </param>
public sealed record InstalledMod(
    ModKey ModId,
    ModVersionKey VersionId,
    string Path,
    string DisplayName,
    long Size,
    DateTimeOffset ModifiedUtc);

/// <summary>A version the repo holds, keyed by what it contains rather than by what it is called.</summary>
public sealed record RegisteredContent(IReadOnlySet<string> Hashes)
{
    public static RegisteredContent None { get; } = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public bool Holds(string? hash) => hash is not null && Hashes.Contains(hash);
}


public enum ModSyncAction
{
    /// <summary>Pinned, installed, and the bytes match. No I/O at all.</summary>
    Keep,

    /// <summary>Pinned and absent.</summary>
    Install,

    /// <summary>
    /// Pinned, installed, and wrong - a different version, or the same version with different bytes.
    /// The file on disk goes first, under the same uninstall rules as anything else.
    /// </summary>
    Replace,

    /// <summary>
    /// Not pinned, and its bytes are registered in the repo, so they can be fetched again. Deleted
    /// once some store on the machine holds them.
    /// </summary>
    UninstallRecoverable,

    /// <summary>
    /// Not pinned and not registered anywhere: a file the user put there that nothing else has a
    /// copy of. Goes to the Recycle Bin, never to delete.
    /// </summary>
    Quarantine
}

public sealed record ModSyncItem
{
    public required ModSyncAction Action { get; init; }
    public required ModKey ModId { get; init; }
    public required string DisplayName { get; init; }

    public ModVersionKey? DesiredVersion { get; init; }
    public string? DesiredHash { get; init; }

    /// <summary>Version-sensitive in the profile. A locked mod at the wrong version risks a savegame.</summary>
    public bool Locked { get; init; }

    public ModVersionKey? InstalledVersion { get; init; }
    public string? InstalledPath { get; init; }

    /// <summary>Null when the file could not be read - which is treated as unrecoverable, not as a match.</summary>
    public string? InstalledHash { get; init; }

    public long InstalledSize { get; init; }

    /// <summary>
    /// Whether the file about to be removed can be fetched again. Also set on <see cref="ModSyncAction.Replace"/>,
    /// whose uninstall half follows exactly the same rules - a replaced file the repo has never seen
    /// is no more disposable than an uninstalled one.
    /// </summary>
    public bool InstalledIsRecoverable { get; init; }

    /// <summary>Whether executing this item sends a file the repo cannot reproduce to the Recycle Bin.</summary>
    public bool DestroysUnrecognisedFile =>
        InstalledPath is not null &&
        InstalledIsRecoverable is false &&
        Action is ModSyncAction.Quarantine or ModSyncAction.Replace;
}


public enum MaterializationMethod
{
    /// <summary>A second directory entry for the store's bytes. Instant, and costs nothing extra.</summary>
    Hardlink,

    /// <summary>The mod folder holds its own bytes. One full copy per install.</summary>
    Copy
}

/// <param name="FellBackToCopy">
/// True only where the user chose a same-disk store and the filesystem refused the link anyway -
/// exFAT, a network path. That is worth surfacing, because the copy cost is being paid without
/// having been chosen. A cross-disk assignment is a deliberate trade and is never warned about.
/// </param>
public sealed record ModMaterialization(MaterializationMethod Method, bool FellBackToCopy);


/// <summary>
/// What sync is about to do, computed and shown before anything is touched.
/// </summary>
public sealed record ModSyncPlan
{
    public required Guid RepoId { get; init; }
    public required Guid ProfileId { get; init; }
    public required Guid InstanceId { get; init; }

    /// <summary>Carried only so the manifest can record it. See <see cref="ModSyncRequest.ProfileName"/>.</summary>
    public string? ProfileName { get; init; }

    public required string ModFolder { get; init; }
    public required IReadOnlyList<ModSyncItem> Items { get; init; }
    public required ModMaterialization Materialization { get; init; }

    /// <summary>Files in the mod folder the adapter does not recognise as mods. Never touched, recorded so drift does not report them.</summary>
    public required IReadOnlyList<string> UnmanagedFileNames { get; init; }

    /// <summary>What the serving store still has to fetch, by hash. Sized before the destructive phase, not during it.</summary>
    public required IReadOnlyList<string> HashesToFetch { get; init; }

    /// <summary>What executing this plan needs, carried on it so nothing has to be resolved twice.</summary>
    public required IInstanceModAdapter Adapter { get; init; }

    /// <summary>The store serving this mod folder's disk - where installs materialise from.</summary>
    public required ContentStore ServingStore { get; init; }

    /// <summary>Every store on the machine, for looking across disks before the network.</summary>
    public required IReadOnlyList<ContentStore> AllStores { get; init; }

    public int KeepCount => Items.Count(x => x.Action is ModSyncAction.Keep);
    public int InstallCount => Items.Count(x => x.Action is ModSyncAction.Install);
    public int ReplaceCount => Items.Count(x => x.Action is ModSyncAction.Replace);
    public int UninstallCount => Items.Count(x => x.Action is ModSyncAction.UninstallRecoverable);
    public int QuarantineCount => Items.Count(x => x.Action is ModSyncAction.Quarantine);

    /// <summary>Everything whose file is about to be sent to the Recycle Bin, by name, for the confirmation.</summary>
    public IReadOnlyList<ModSyncItem> Unrecognised => [.. Items.Where(x => x.DestroysUnrecognisedFile)];

    /// <summary>
    /// Whether anything at all changes. A plan of nothing but <see cref="ModSyncAction.Keep"/> is
    /// still worth showing - it is the answer "your folder already matches".
    /// </summary>
    public bool HasWork => Items.Any(x => x.Action is not ModSyncAction.Keep);
}


public enum ModSyncPhase
{
    /// <summary>Filling the serving store. Nothing in the mod folder has been touched yet.</summary>
    Fetching,

    Removing,
    Installing,

    /// <summary>Manifest and store housekeeping.</summary>
    Finishing
}

/// <param name="Completed">How many mods of <paramref name="Total"/> this phase has finished.</param>
public sealed record ModSyncProgress(ModSyncPhase Phase, int Completed, int Total)
{
    public string? ModId { get; init; }
    public string? Detail { get; init; }
    public long BytesTransferred { get; init; }
    public long TotalBytes { get; init; }
}


public enum QuarantineDestination
{
    RecycleBin,

    /// <summary>Where the Recycle Bin was unavailable - a drive with it turned off, a network path.</summary>
    QuarantineFolder,

    /// <summary>Neither worked. The file is still in the mod folder, which is the safe end of the failure.</summary>
    Failed
}

public sealed record QuarantinedFile(ModKey ModId, string OriginalPath, QuarantineDestination Destination)
{
    /// <summary>Where it went, for the folder case. Null for the Recycle Bin, which the user opens themselves.</summary>
    public string? Path { get; init; }
}

public sealed record ModSyncFailure(ModKey ModId, ModSyncAction Action, string Message)
{
    public Exception? Exception { get; init; }
}

/// <param name="Completed">
/// True only when every item ran without failure. The manifest is written on nothing less: a partial
/// one would claim a state that never existed, where leaving the previous one makes the next check
/// report drift - which is true, and re-applying fixes.
/// </param>
public sealed record ModSyncResult(bool Completed, IReadOnlyList<ModSyncFailure> Failures)
{
    public IReadOnlyList<QuarantinedFile> Quarantined { get; init; } = [];

    /// <summary>Null when the destructive phase never ran, so nothing was touched.</summary>
    public ContentStoreEvictionResult? Eviction { get; init; }

    public bool ManifestWritten { get; init; }
}
