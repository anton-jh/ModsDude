using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Sync;

public interface IStoreIntegrityService
{
    /// <summary>Raised, on the thread that found it, when <see cref="Found"/> grows.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Every rewritten store blob caught this session, however it was caught, deduplicated by address.
    /// </summary>
    /// <remarks>
    /// <b>Accumulated rather than recomputed.</b> A corrupt blob is deleted the moment it is found,
    /// so the very next check cannot see it - the evidence destroys itself, by design, because
    /// leaving it would go on serving wrong bytes to every repo on the volume. A finding that
    /// vanished on the next check would be one nobody ever read.
    /// </remarks>
    IReadOnlyList<CorruptedBlob> Found { get; }

    /// <summary>
    /// Checks the files of one target's last apply that changed since, and drops any blob that
    /// proves to have been rewritten through its hardlink.
    /// </summary>
    /// <remarks>
    /// Runs off the target's manifest alone, so it answers whatever the folder's drift status is.
    /// Copy-served games fall out for free rather than by a special case: nothing there is
    /// hardlinked, so the installed file is never the same file as the blob and every candidate is
    /// discarded by the identity comparison - which is also what happens on a platform or filesystem
    /// that cannot answer the question at all.
    /// </remarks>
    /// <returns>What this check found.</returns>
    Task<IReadOnlyList<CorruptedBlob>> CheckAsync(ModTargetRef target, CancellationToken cancellationToken);

    /// <summary>
    /// Marks every manifest entry running one of these addresses from <paramref name="store"/> as
    /// suspect, once the blobs behind them were found corrupt and removed, so the next apply hashes
    /// those files again rather than keeping them.
    /// </summary>
    void MarkSuspect(ContentStore store, IReadOnlySet<string> hashes);
}
