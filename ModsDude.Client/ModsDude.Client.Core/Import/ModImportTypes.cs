using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;

namespace ModsDude.Client.Core.Import;

/// <summary>What to import, and how to ask about the parts that cannot be decided without a person.</summary>
/// <param name="Versions">
/// The catalog rows the user picked. Each is one mod version; several versions of one mod in the
/// same batch is ordinary - one from a mod folder, one from Downloads.
/// </param>
/// <param name="Comparer">The repo adapter's, so ordering follows the game's own numbering.</param>
public sealed record ModImportRequest(
    Guid RepoId,
    IReadOnlyList<CatalogModVersion> Versions,
    IModVersionComparer Comparer)
{
    /// <summary>
    /// Per mod version, not per import. At two thousand mods one global spinner cannot tell a
    /// working import from a hung one.
    /// </summary>
    public IProgress<ModImportProgress>? Progress { get; init; }

    /// <summary>
    /// Asked once, for every mod at once, and only for the mods the comparer could not settle. Null
    /// - or a null answer - skips those mods and keeps the rest of the batch.
    /// </summary>
    public ModVersionArbitrationResolver? ResolveArbitration { get; init; }

    /// <summary>
    /// Asked once, for every version whose sources hold genuinely different files. Null - or a null
    /// answer - skips those versions and keeps the rest of the batch, exactly as the import did for
    /// all of them before there was anything to ask.
    /// </summary>
    public ModSourceConflictResolver? ResolveSourceConflicts { get; init; }

    /// <summary>
    /// How many mods run their link, upload and register in parallel. Network-bound, so a fixed
    /// handful rather than the processor count the folder scan uses.
    /// </summary>
    public int MaxConcurrentMods { get; init; } = 5;

    /// <summary>
    /// How many times a mod may lose the placement race before its remaining versions are reported
    /// instead. Bounded because two members inserting into the same mod in a loop would otherwise
    /// keep each other going indefinitely.
    /// </summary>
    public int MaxPlacementRetries { get; init; } = 4;
}

/// <returns>
/// The intended final order per mod, covering every registered and incoming version. Mods left out
/// of the answer - and a null answer, which is what cancelling the dialog produces - are skipped.
/// </returns>
public delegate Task<IReadOnlyDictionary<ModKey, IReadOnlyList<ModVersionKey>>?> ModVersionArbitrationResolver(
    IReadOnlyList<ModVersionArbitrationItem> items,
    CancellationToken cancellationToken);


/// <summary>
/// One version that several sources hold genuinely different files for.
/// </summary>
/// <remarks>
/// Only one file can ever be registered under a (mod, version), so this is a question with no
/// default the import is entitled to pick - the two builds wear the same id and the same version
/// string, and nothing but a person knows which one they meant.
/// </remarks>
public sealed record ModSourceConflict(
    CatalogModVersion Version,
    IReadOnlyList<ModFileCandidate> Candidates);

/// <returns>
/// Which candidate to import per version, by <see cref="ModFileCandidate.Key"/>. Versions left out
/// of the answer - and a null answer, which is what dismissing the dialog produces - are skipped,
/// and skipping recycles nothing.
/// </returns>
public delegate Task<IReadOnlyDictionary<ModVersionIdentity, string>?> ModSourceConflictResolver(
    IReadOnlyList<ModSourceConflict> conflicts,
    CancellationToken cancellationToken);


public enum ModImportPhase
{
    Queued,
    Linking,
    Uploading,
    Registering,

    /// <summary>Registered already; whatever happens here cannot fail the version.</summary>
    PublishingImagery,

    Completed,
    Failed,
    Skipped
}

/// <param name="TotalBytes">The archive's size, so a row can show a proportion. Zero outside upload.</param>
public sealed record ModImportProgress(ModVersionIdentity Identity, ModImportPhase Phase)
{
    public long BytesTransferred { get; init; }

    public long TotalBytes { get; init; }

    public string? Error { get; init; }
}


public enum ModImportStatus
{
    /// <summary>This import registered it.</summary>
    Registered,

    /// <summary>
    /// It was registered already - by an earlier run, or by a teammate mid-import. A success either
    /// way: the bytes this import wanted are in the repo.
    /// </summary>
    AlreadyRegistered,

    /// <summary>
    /// Two sources hold different files claiming this mod and version. Only one can be registered,
    /// so the user picks rather than the import picking silently.
    /// </summary>
    SourceConflict,

    /// <summary>
    /// A file is already stored under this mod and version and it is not the one being imported.
    /// Registering would record a hash describing bytes nobody can download, with no way back.
    /// </summary>
    ContentMismatch,

    /// <summary>The mod's version ordering was never settled, so nothing was placed.</summary>
    NeedsArbitration,

    /// <summary>Selected without a local file to upload.</summary>
    NoLocalFile,

    Failed
}

public sealed record ModImportItemResult(ModVersionIdentity Identity, ModImportStatus Status)
{
    /// <summary>
    /// Why, for the statuses that need explaining - and, on an otherwise successful item, a note
    /// that its imagery did not make it.
    /// </summary>
    public string? Message { get; init; }

    public Exception? Exception { get; init; }

    public bool IsSuccess => Status is ModImportStatus.Registered or ModImportStatus.AlreadyRegistered;
}

/// <summary>
/// A local file the user chose against when two sources disagreed about one version, and which is
/// therefore due for the Recycle Bin.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported rather than recycled.</b> The import knows the choice was made and the registration
/// succeeded; it does not know whether the action the user actually took has finished. In the
/// profile editor the import is the first half of a save, and a file removed before the save
/// commits is a file removed for a save that never happened. So the caller recycles, once its own
/// work is done - see <see cref="ModImportService.RecycleSupersededAsync"/>.
/// </para>
/// <para>
/// <b>Only rejected bytes.</b> Copies that are byte-identical to the one imported are left alone.
/// The user was asked which of several <em>different</em> files to keep and answered that; deleting
/// their duplicates of the winner would be tidying they did not ask for.
/// </para>
/// </remarks>
public sealed record ModSupersededFile(ModVersionIdentity Identity, string FilePath, string SourceName);

/// <summary>
/// Everything the import did, whether or not all of it worked. One mod failing never takes the batch
/// with it, so a partly-failed import is the ordinary case rather than an exception.
/// </summary>
public sealed record ModImportResult(IReadOnlyList<ModImportItemResult> Items)
{
    /// <summary>
    /// Files the user chose against, for versions that then imported successfully. Empty in every
    /// ordinary run - it takes two sources holding different builds under one version to fill it.
    /// </summary>
    public IReadOnlyList<ModSupersededFile> Superseded { get; init; } = [];

    public IReadOnlyList<ModImportItemResult> Succeeded => [.. Items.Where(x => x.IsSuccess)];

    public IReadOnlyList<ModImportItemResult> Unfinished => [.. Items.Where(x => !x.IsSuccess)];

    public bool AnythingFailed => Items.Any(x => !x.IsSuccess);
}
