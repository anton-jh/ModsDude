using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Sync;

public interface IStoreIntegrityService
{
    /// <summary>
    /// Checks the changed files of one of a game's folders, and drops any blob that proves to have
    /// been rewritten.
    /// </summary>
    /// <param name="changed">
    /// The names <see cref="DriftService"/> found no longer matching the manifest. Nothing
    /// else can have been rewritten: a file whose size and time still match what was installed is
    /// one nothing has written to.
    /// </param>
    /// <remarks>
    /// Copy-served games fall out for free rather than by a special case. Nothing there is
    /// hardlinked, so the installed file is never the same file as the blob and every candidate is
    /// discarded by the identity comparison - which is also what happens on a platform or filesystem
    /// that cannot answer the question at all.
    /// </remarks>
    Task<IReadOnlyList<CorruptedBlob>> CheckAsync(
        ModTargetRef target,
        string modFolder,
        IReadOnlyList<string> changed,
        CancellationToken cancellationToken);
}
