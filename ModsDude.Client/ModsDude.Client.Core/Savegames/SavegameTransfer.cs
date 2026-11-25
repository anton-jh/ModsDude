using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Disks;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameTransfer(
    IFilesClient filesClient,
    IModFileDownloader downloader,
    IModFileUploader uploader,
    ISavegamePacker packer,
    ISavegameRecycler recycler,
    IDiskSpaceGuard diskSpaceGuard,
    ILogger<SavegameTransfer> logger)
    : ISavegameTransfer
{
    /// <remarks>
    /// Staged to a temporary file because a zip is read from its central directory at the end, and a
    /// network stream cannot seek back. The contents are verified as they are unpacked, which is the
    /// only point where what arrived and what lands in the slot are the same bytes.
    /// </remarks>
    public Task<DownloadedSavegame> DownloadAsync(
        Guid repoId,
        Guid savegameId,
        string contentHash,
        IProgress<SavegameProgress>? progress,
        CancellationToken ct)
        => Task.Run(() => DownloadToArchiveAsync(repoId, savegameId, contentHash, progress, ct), ct);

    private async Task<DownloadedSavegame> DownloadToArchiveAsync(
        Guid repoId,
        Guid savegameId,
        string contentHash,
        IProgress<SavegameProgress>? progress,
        CancellationToken ct)
    {
        var link = await filesClient.CreateSavegameDownloadLinkV1Async(new CreateSavegameDownloadLinkRequest
        {
            RepoId = repoId,
            SavegameId = savegameId,
            ContentHash = contentHash
        }, ct);

        var archivePath = SavegamePacker.GetTemporaryArchivePath();

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        try
        {
            using (var download = await downloader.OpenAsync(link.Link, null, ct))
            {
                // An unknown length goes unchecked, and a disk that fills fails the write instead - still
                // before the slot is touched.
                var length = download.Length ?? 0;

                diskSpaceGuard.EnsureRoomFor(DiskSpaceNeeds.For(archivePath, length));

                await using var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous);

                // Counted as written rather than through the downloader's own progress, which runs
                // ahead of what the reader has been handed.
                var received = 0L;

                progress?.Report(new SavegameProgress(SavegameStage.Downloading, 0, length));

                await ReportingCopy.CopyAsync(
                    download.Content,
                    file,
                    progress is null
                        ? null
                        : bytes => progress.Report(new SavegameProgress(SavegameStage.Downloading, received += bytes, length)),
                    ct);
            }

            return new DownloadedSavegame(archivePath, contentHash, path => FileSystemHelper.TryDeleteFile(path, logger));
        }
        catch (Exception)
        {
            FileSystemHelper.TryDeleteFile(archivePath, logger);

            throw;
        }
    }

    public async Task<DisplacedSavegame?> WriteIntoSlotAsync(
        DownloadedSavegame downloaded,
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        SavegameSlotId slot,
        IProgress<SavegameProgress>? progress,
        CancellationToken ct)
    {
        var slotPath = adapter.GetSlotPath(target, slot);

        diskSpaceGuard.EnsureRoomFor(DiskSpaceNeeds.For(slotPath, SavegamePacker.MeasureUnpacked(downloaded.ArchivePath)));

        string? displaced;

        try
        {
            displaced = await packer.UnpackAsync(downloaded.ArchivePath, downloaded.ContentHash, adapter, target, slot, ct, progress);
        }
        catch (InvalidDataException exception)
        {
            throw new UserFriendlyException(
                "The downloaded savegame does not match",
                $"The archive for contents '{downloaded.ContentHash}' did not unpack to them: {exception.Message}",
                exception);
        }

        return displaced is null ? null : recycler.PutAway(displaced, slotPath);
    }

    /// <remarks>
    /// The blob is addressed by its content, so an <c>AlreadyStored</c> link skips the upload entirely.
    /// </remarks>
    public Task<Guid?> UploadAsync(Guid repoId, Guid savegameId, PackedSavegame packed, IProgress<SavegameProgress>? progress, CancellationToken ct)
        => Task.Run(() => UploadArchiveAsync(repoId, savegameId, packed, progress, ct), ct);

    private async Task<Guid?> UploadArchiveAsync(Guid repoId, Guid savegameId, PackedSavegame packed, IProgress<SavegameProgress>? progress, CancellationToken ct)
    {
        Task<CreateSavegameUploadLinkResponse> CreateLinkAsync(Guid? uploadId, CancellationToken cancellationToken)
            => filesClient.CreateSavegameUploadLinkV1Async(new CreateSavegameUploadLinkRequest
            {
                RepoId = repoId,
                SavegameId = savegameId,
                ContentHash = packed.ContentHash,
                SizeBytes = packed.SizeBytes,
                UploadId = uploadId
            }, cancellationToken);

        var link = await CreateLinkAsync(null, ct);

        if (link.AlreadyStored)
        {
            return null;
        }

        if (link.Link is not string destination || link.UploadId is not Guid uploadId)
        {
            throw new UserFriendlyException(
                "Could not upload the savegame",
                $"CreateSavegameUploadLink answered with neither a link and upload nor alreadyStored for '{packed.ContentHash}'.");
        }

        progress?.Report(new SavegameProgress(SavegameStage.Uploading, 0, packed.SizeBytes));

        try
        {
            await uploader.UploadAsync(
                new ModFileUpload(destination, link.ContentHashMetadataKey, () => File.OpenRead(packed.FilePath))
                {
                    BytesTransferred = progress is null
                        ? null
                        : new InlineProgress<long>(sent => progress.Report(new SavegameProgress(SavegameStage.Uploading, sent, packed.SizeBytes))),
                    // The address is the contents, so an archive that changed since packing must never
                    // be copied there: no snapshot may point at a blob nobody can reproduce.
                    ExpectedHash = packed.ArchiveHash,
                    RenewLink = async cancellationToken => (await CreateLinkAsync(uploadId, cancellationToken)).Link
                        ?? throw new BlobAlreadyExistsException("The savegame was stored by another check-in while this one was uploading.")
                },
                ct);

            return uploadId;
        }
        catch (BlobAlreadyExistsException exception)
        {
            // Another check-in of the same contents stored them first. The address is their hash, so
            // what is stored is this save.
            logger.LogInformation(exception, "Savegame {SavegameId} content {ContentHash} was already stored in repo {RepoId}.", savegameId, packed.ContentHash, repoId);

            return null;
        }
        catch (UploadedContentChangedException exception)
        {
            throw new UserFriendlyException(
                "The savegame changed while it was being uploaded",
                $"Packed as '{exception.ExpectedHash}' but uploaded '{exception.ActualHash}'. No snapshot has been recorded.");
        }
    }

    public void SweepTemporaryArchives()
    {
        var folder = SavegamePacker.TemporaryArchiveFolder;

        try
        {
            if (Directory.Exists(folder) is false)
            {
                return;
            }

            foreach (var archive in Directory.EnumerateFiles(folder, "*.zip"))
            {
                FileSystemHelper.TryDeleteFile(archive, logger);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not look for leftover savegame archives in {Folder}.", folder);
        }
    }
}
