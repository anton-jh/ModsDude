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

    /// <summary>
    /// What the repo registered the file as being called, and therefore what the mod folder has to
    /// call it. Null where the repo has nothing usable, which leaves the name to the adapter.
    /// </summary>
    public ModFileName? FileName { get; init; }

    /// <summary>
    /// How big the registered file is, for saying what an apply will download before it starts. Null
    /// where the repo does not know - a version registered before sizes were recorded, until the server
    /// backfills it - which is reported as unknown rather than counted as nothing.
    /// </summary>
    public long? SizeBytes { get; init; }
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

    /// <summary>
    /// Pinned, installed, the bytes match - and the file is under the wrong name. One directory
    /// operation; nothing is fetched, copied or removed.
    /// </summary>
    /// <remarks>
    /// Its own action rather than a quiet fixup inside <see cref="Keep"/>, because a plan of nothing
    /// but keeps reports "already correct" and is never executed. A folder that an older client
    /// lower-cased is corrected by exactly this, on the next apply.
    /// </remarks>
    Rename,

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

    /// <summary>The registered size of <see cref="DesiredHash"/>, where the repo knows it.</summary>
    public long? DesiredSize { get; init; }

    /// <summary>
    /// What the file has to be called once this item has run. Null where the repo registered nothing
    /// usable, which leaves the name to the adapter. See <see cref="ModFileName"/>.
    /// </summary>
    public ModFileName? FileName { get; init; }

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

    /// <summary>Which game this plan is for. Its holds are what can refuse the apply.</summary>
    public required GameIdentity Game { get; init; }

    /// <summary>Carried only so the manifest can record it. See <see cref="ModSyncRequest.ProfileName"/>.</summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// Which revision of the profile this plan was built from - read with the dependencies, so it
    /// describes the list that was planned rather than whatever the profile is on by the time the
    /// plan runs. Recorded in the manifest.
    /// </summary>
    public int? ProfileRevision { get; init; }

    /// <summary>The target this plan is for - one folder, which is genuinely sync's unit of work.</summary>
    public required ModTarget Target { get; init; }

    /// <summary>Which folder of which game, and therefore which manifest this rewrites.</summary>
    public ModTargetRef TargetRef => new(Game, Target.Key);

    /// <summary>The folder itself, which is all most of the plan's readers want from the target.</summary>
    public string ModFolder => Target.Path;
    public required IReadOnlyList<ModSyncItem> Items { get; init; }
    public required ModMaterialization Materialization { get; init; }

    /// <summary>Files in the mod folder the adapter does not recognise as mods. Never touched, recorded so drift does not report them.</summary>
    public required IReadOnlyList<string> UnmanagedFileNames { get; init; }

    /// <summary>What the serving store still has to fetch, by hash. Sized before the destructive phase, not during it.</summary>
    public required IReadOnlyList<string> HashesToFetch { get; init; }

    /// <summary>
    /// What the fetch phase will take off the network, as opposed to <see cref="HashesToFetch"/>, which
    /// also counts what it will copy from another disk's store. Worked out here, before anything is
    /// touched, so that the confirmation can say how much there is.
    /// </summary>
    public PlannedDownloads Downloads => PlannedDownloads.For(this);

    /// <summary>What executing this plan needs, carried on it so nothing has to be resolved twice.</summary>
    public required ILocalModAdapter Adapter { get; init; }

    /// <summary>The store serving this mod folder's disk - where installs materialise from.</summary>
    public required ContentStore ServingStore { get; init; }

    /// <summary>Every store on the machine, for looking across disks before the network.</summary>
    public required IReadOnlyList<ContentStore> AllStores { get; init; }

    public int KeepCount => Items.Count(x => x.Action is ModSyncAction.Keep);
    public int RenameCount => Items.Count(x => x.Action is ModSyncAction.Rename);
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
    /// <summary>
    /// Working out what would change. Nothing has been decided yet, let alone touched.
    /// </summary>
    /// <remarks>
    /// <b>Reported because it is not fast.</b> A folder whose files no longer match the manifest -
    /// a first apply, or one the user populated by hand - is read in full to be hashed, which on a
    /// Farming Simulator mod folder is minutes. It used to happen behind a still window with the
    /// confirmation appearing at the end of it, which reads as a hang rather than as work.
    /// </remarks>
    Planning,

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

    /// <summary>
    /// Other items of this phase are in flight too - the fetch phase, which runs several at once. A
    /// report naming this item says nothing about the others, which end with their own
    /// <see cref="ItemFinished"/>. Where it is false, a report naming the next item ends the last.
    /// </summary>
    public bool Concurrent { get; init; }

    /// <summary>The last report for <see cref="ModId"/> in a <see cref="Concurrent"/> phase.</summary>
    public bool ItemFinished { get; init; }
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


/// <summary>
/// The mods an apply has to download, and how much that is.
/// </summary>
/// <param name="Count">How many files. Distinct by content: two mods sharing bytes are fetched once.</param>
/// <param name="KnownBytes">
/// The sum of the sizes the repo could give. A lower bound while <paramref name="UnknownCount"/> is
/// above zero.
/// </param>
/// <param name="UnknownCount">How many of them the repo has no size for.</param>
public sealed record PlannedDownloads(int Count, long KnownBytes, int UnknownCount)
{
    public static PlannedDownloads None { get; } = new(0, 0, 0);

    public bool IsAny => Count > 0;

    /// <summary>Whether <see cref="KnownBytes"/> is the whole of it.</summary>
    public bool IsComplete => UnknownCount == 0;

    /// <summary>
    /// Everything that will come off the network: what the fetch phase would fetch, less what another
    /// store on the machine already holds - which it copies from disk instead of downloading.
    /// </summary>
    public static PlannedDownloads For(ModSyncPlan plan)
        => Across([plan]);

    /// <summary>
    /// The same across every folder one apply reaches. <b>Distinct by content across the lot</b>: a mod
    /// two folders both want is downloaded for the first and copied from its store for the second, so
    /// adding the folders' own counts up would say a mod was downloaded twice.
    /// </summary>
    public static PlannedDownloads Across(IEnumerable<ModSyncPlan> plans)
    {
        var sizes = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            var wanted = plan.HashesToFetch
                .Where(hash => plan.AllStores.Any(store => store.Contains(hash)) is false)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in plan.Items.Where(x => x.DesiredHash is not null && wanted.Contains(x.DesiredHash)))
            {
                // One folder may know a size that another does not, so a known one is never
                // overwritten by an unknown one.
                sizes[item.DesiredHash!] = item.DesiredSize ?? sizes.GetValueOrDefault(item.DesiredHash!);
            }
        }

        return sizes.Count == 0
            ? None
            : new PlannedDownloads(sizes.Count, sizes.Values.Sum(x => x ?? 0), sizes.Values.Count(x => x is null));
    }
}
