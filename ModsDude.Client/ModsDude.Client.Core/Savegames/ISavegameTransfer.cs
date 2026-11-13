using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Moves savegame bytes between storage and a slot.</summary>
public interface ISavegameTransfer
{
    /// <summary>
    /// Fetches a snapshot's blob into a temporary archive, touching no slot. Split from
    /// <see cref="WriteIntoSlotAsync"/> so the caller can look again - is the game running, is the slot
    /// still writable - after the slow part and right before the slot changes.
    /// </summary>
    Task<DownloadedSavegame> DownloadAsync(
        Guid repoId,
        Guid savegameId,
        string contentHash,
        IProgress<SavegameProgress>? progress,
        CancellationToken ct);

    /// <summary>
    /// Replaces the slot's contents with a downloaded snapshot, once its contents are verified to be
    /// the ones it was fetched as.
    /// </summary>
    /// <returns>Where the slot's previous contents went, or null where it was empty.</returns>
    Task<DisplacedSavegame?> WriteIntoSlotAsync(
        DownloadedSavegame downloaded,
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        SavegameSlotId slot,
        IProgress<SavegameProgress>? progress,
        CancellationToken ct);

    /// <summary>
    /// Puts a packed savegame in storage, or establishes that the same contents are already there.
    /// </summary>
    Task UploadAsync(Guid repoId, Guid savegameId, PackedSavegame packed, IProgress<SavegameProgress>? progress, CancellationToken ct);

    /// <summary>
    /// Deletes the temporary archives a run that crashed or was killed left behind. Only while
    /// nothing is packing or transferring a savegame, which at startup nothing is.
    /// </summary>
    void SweepTemporaryArchives();
}


/// <summary>A snapshot fetched into a temporary archive. Dispose deletes the archive.</summary>
public sealed class DownloadedSavegame(string archivePath, string contentHash, Action<string> delete) : IDisposable
{
    public string ArchivePath { get; } = archivePath;

    public string ContentHash { get; } = contentHash;

    public void Dispose() => delete(ArchivePath);
}
