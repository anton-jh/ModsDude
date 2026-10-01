using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Import;

public interface IModImportService
{
    Task<ModImportResult> ImportAsync(ModImportRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the files the user chose against to the Recycle Bin, once whatever they were doing has
    /// actually succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called by the caller, not by the import.</b> The import knows a version registered; it does
    /// not know whether the profile save that registration was the first half of has committed. A
    /// file removed for a save that never happened is a file removed for nothing.
    /// </para>
    /// <para>
    /// <b>Best-effort, and never fatal.</b> A file the game is holding open stays where it is, which
    /// costs a duplicate on disk and nothing else - the repo already has the bytes that matter.
    /// Nothing here throws, and everything it could not do is in the log.
    /// </para>
    /// </remarks>
    /// <returns>How many files reached the Recycle Bin.</returns>
    int RecycleSuperseded(IReadOnlyList<ModSupersededFile> superseded);

    /// <summary>
    /// The same import, invalidating the catalog it was selected from when it is over.
    /// </summary>
    /// <remarks>
    /// In a <c>finally</c> deliberately: a cancelled or partly failed import still registered
    /// something, and a catalog that kept claiming otherwise would offer those versions for import
    /// all over again.
    /// </remarks>
    Task<ModImportResult> ImportAsync(ModCatalog catalog, ModImportRequest request, CancellationToken cancellationToken);
}
