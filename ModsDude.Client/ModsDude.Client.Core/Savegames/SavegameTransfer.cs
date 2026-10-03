using Microsoft.Extensions.Logging;
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
    ILogger<SavegameTransfer> logger)
    : ISavegameTransfer
{
    /// <remarks>
    /// Staged to a temporary file because a zip is read from its central directory at the end, and a
    /// network stream cannot seek back. Hashed on the way past, so verifying costs no second read.
    /// </remarks>
    public async Task<DisplacedSavegame?> DownloadIntoSlotAsync(
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        Guid repoId,
        Guid savegameId,
        string contentHash,
        SavegameSlotId slot,
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
            await using (var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous))
            {
                // Counted as written rather than through the downloader's own progress, which runs
                // ahead of what the reader has been handed.
                var length = download.Length ?? 0;
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

            await using (var written = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var size = written.Length;

                progress?.Report(new SavegameProgress(SavegameStage.Verifying, 0, size));

                var actual = await ModContentHasher.ComputeAsync(
                    written,
                    progress is null ? null : new InlineProgress<long>(read => progress.Report(new SavegameProgress(SavegameStage.Verifying, read, size))),
                    ct);

                if (ModContentHasher.Matches(actual, contentHash) is false)
                {
                    throw new UserFriendlyException(
                        "The savegame that arrived is not the one that was asked for",
                        $"Expected content hash '{contentHash}' but the downloaded blob hashes to '{actual}'. Nothing has been written into the slot.");
                }
            }

            return await packer.UnpackAsync(archivePath, adapter, target, slot, ct, progress) is string displaced
                ? recycler.PutAway(displaced)
                : null;
        }
        finally
        {
            FileSystemHelper.TryDeleteFile(archivePath, logger);
        }
    }

    /// <remarks>
    /// The blob is addressed by its content, so an <c>AlreadyStored</c> link skips the upload entirely.
    /// </remarks>
    public async Task UploadAsync(Guid repoId, Guid savegameId, PackedSavegame packed, IProgress<SavegameProgress>? progress, CancellationToken ct)
    {
        var link = await filesClient.CreateSavegameUploadLinkV1Async(new CreateSavegameUploadLinkRequest
        {
            RepoId = repoId,
            SavegameId = savegameId,
            ContentHash = packed.ContentHash,
            SizeBytes = packed.SizeBytes
        }, ct);

        if (link.AlreadyStored)
        {
            return;
        }

        if (link.Link is not string destination)
        {
            throw new UserFriendlyException(
                "The server did not offer anywhere to upload the savegame",
                $"CreateSavegameUploadLink answered with neither a link nor alreadyStored for '{packed.ContentHash}'.");
        }

        progress?.Report(new SavegameProgress(SavegameStage.Uploading, 0, packed.SizeBytes));

        var uploaded = await uploader.UploadAsync(
            new ModFileUpload(destination, link.ContentHashMetadataKey, () => File.OpenRead(packed.FilePath))
            {
                BytesTransferred = progress is null
                    ? null
                    : new InlineProgress<long>(sent => progress.Report(new SavegameProgress(SavegameStage.Uploading, sent, packed.SizeBytes)))
            },
            ct);

        // The uploader hashes what it sent. A mismatch means the archive changed between the two reads,
        // and no snapshot may point at a blob nobody can reproduce.
        if (ModContentHasher.Matches(uploaded, packed.ContentHash) is false)
        {
            throw new UserFriendlyException(
                "The savegame changed while it was being uploaded",
                $"Packed as '{packed.ContentHash}' but uploaded '{uploaded}'. No snapshot has been recorded.");
        }
    }
}
