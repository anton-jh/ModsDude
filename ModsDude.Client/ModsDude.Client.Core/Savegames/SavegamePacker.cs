using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using System.IO.Compression;
using System.Security.Cryptography;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// One slot's contents, zipped and ready to upload.
/// </summary>
/// <param name="FilePath">
/// A temporary file the <b>caller owns</b>: nothing here deletes it, and nothing here will hand it
/// back a second time. Delete it once the upload has been verified, or on the way out of a failure.
/// </param>
/// <param name="ContentHash">
/// The SHA-256 of the file at <paramref name="FilePath"/>, lowercase hex - the address the blob is
/// stored under and the value a check-in compares against the head.
/// </param>
public readonly record struct PackedSavegame(string FilePath, string ContentHash, long SizeBytes);


/// <summary>
/// Turns a savegame slot into an archive and back again.
/// </summary>
/// <remarks>
/// <para>
/// <b>The archive is deterministic.</b> Identical slot contents produce identical bytes and therefore
/// an identical hash, whoever packed them and whenever. Everything else in savegames rests on that:
/// a check-in whose hash equals the head's mints no version, so launching the game and quitting must
/// not cost a 400 MB blob and a line of history - and a drift check can only ask "has this been
/// played?" if replaying the same bytes answers no. Two things buy it, and both are load-bearing:
/// entries sorted <see cref="StringComparer.Ordinal"/> rather than in whatever order the filesystem
/// enumerated them, and every entry timestamp pinned to <see cref="_fixedTimestamp"/> rather than
/// carrying the file's own - a save copied between machines keeps its contents and loses its mtimes.
/// </para>
/// <para>
/// <b>Hashing and packing walk the same code.</b> <see cref="HashSlotAsync"/> is
/// <see cref="PackAsync"/> writing to nowhere, so a drift check cannot disagree with the packer -
/// which would report play that is not there, or, worse, miss play that is.
/// See docs/PLAN.md#phase-8--savegames.
/// </para>
/// </remarks>
public interface ISavegamePacker
{
    /// <summary>
    /// Zips everything in <paramref name="slot"/> that the adapter says belongs in a packed save,
    /// recursively, into a temporary file.
    /// </summary>
    /// <remarks>
    /// A slot folder that does not exist, or holds nothing that belongs, packs as an empty archive
    /// rather than throwing. Its hash is a real hash that no played save shares, which is a more
    /// useful answer than an exception to the one caller - the drift check - that can legitimately
    /// ask about a slot somebody deleted from under it.
    /// </remarks>
    Task<PackedSavegame> PackAsync(IInstanceSavegameAdapter adapter, SavegameSlotId slot, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the slot's contents with the archive's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The slot ends up holding exactly the archive, and nothing else.</b> Whatever was there is
    /// permanently deleted - not recycled, not quarantined. That is deliberate: by the time this runs
    /// the caller has already decided what displacing this slot means, and it is the caller that owes
    /// the user the confirmation and the trip to <c>IRecycleBin</c>. A second, silent safety net in
    /// here would only make the first one look optional.
    /// </para>
    /// <para>
    /// An entry that would resolve outside the slot folder is refused rather than written, and
    /// refusing aborts the whole unpack - the slot is left as it was.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidDataException">An entry names a path outside the slot folder.</exception>
    Task UnpackAsync(string archivePath, IInstanceSavegameAdapter adapter, SavegameSlotId slot, CancellationToken cancellationToken);

    /// <summary>
    /// What <see cref="PackAsync"/> would report as the content hash, without keeping the archive.
    /// </summary>
    /// <remarks>
    /// The cheap half of "has this slot been played since it was checked in?". Still reads and
    /// compresses every byte - the hash has to be over the same bytes the packer produces or the two
    /// could differ - but writes none of them.
    /// </remarks>
    Task<string> HashSlotAsync(IInstanceSavegameAdapter adapter, SavegameSlotId slot, CancellationToken cancellationToken);
}


/// <inheritdoc cref="ISavegamePacker"/>
public sealed class SavegamePacker(ILogger<SavegamePacker>? logger = null) : ISavegamePacker
{
    private readonly ILogger _log = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>
    /// Every entry's timestamp, in place of the file's own.
    /// </summary>
    /// <remarks>
    /// The zero of DOS time, which is what deterministic archivers everywhere use for "no timestamp".
    /// Written as UTC so the bytes do not depend on the machine's time zone, and a save's real mtimes
    /// are worth nothing anyway: copying one between two members' machines rewrites all of them while
    /// changing nothing that was played.
    /// </remarks>
    private static readonly DateTimeOffset _fixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const int _bufferSize = 64 * 1024;


    public async Task<PackedSavegame> PackAsync(IInstanceSavegameAdapter adapter, SavegameSlotId slot, CancellationToken cancellationToken)
    {
        var archivePath = GetTemporaryArchivePath();

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        try
        {
            string hash;

            await using (var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, _bufferSize, FileOptions.Asynchronous))
            {
                hash = await WriteArchiveAsync(adapter, slot, file, cancellationToken);
            }

            return new PackedSavegame(archivePath, hash, new FileInfo(archivePath).Length);
        }
        catch (Exception)
        {
            // The caller owns the file only once it has one. A half-written archive nobody was told
            // about is this method's to clean up - including on cancellation.
            TryDeleteFile(archivePath);

            throw;
        }
    }

    public Task<string> HashSlotAsync(IInstanceSavegameAdapter adapter, SavegameSlotId slot, CancellationToken cancellationToken)
    {
        // Literally the pack, discarding the bytes as they are produced. Sharing the writer rather
        // than hashing the files individually is the whole point: any difference between the two -
        // an exclusion applied in one, an ordering rule in the other - would surface as a slot that
        // reads as played the moment it is checked in.
        return WriteArchiveAsync(adapter, slot, Stream.Null, cancellationToken);
    }

    public async Task UnpackAsync(string archivePath, IInstanceSavegameAdapter adapter, SavegameSlotId slot, CancellationToken cancellationToken)
    {
        var slotPath = Path.GetFullPath(adapter.GetSlotPath(slot));
        var parent = Path.GetDirectoryName(slotPath)
            ?? throw new ArgumentException($"'{slotPath}' is a filesystem root, not a savegame slot.", nameof(slot));

        // Staged beside the slot rather than in the system temp folder, so landing it is a rename on
        // one volume instead of a second copy of a save that can be hundreds of megabytes. The
        // displaced folder is moved aside rather than deleted first, so a failure anywhere up to the
        // last rename leaves the slot exactly as it was.
        var staging = Path.Combine(parent, $".modsdude-unpack-{Guid.NewGuid():N}");
        var displaced = Path.Combine(parent, $".modsdude-replaced-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(staging);

            await ExtractAsync(archivePath, staging, cancellationToken);

            if (Directory.Exists(slotPath))
            {
                Directory.Move(slotPath, displaced);
            }

            Directory.Move(staging, slotPath);
        }
        catch (Exception)
        {
            TryDeleteDirectory(staging);

            if (Directory.Exists(displaced) && Directory.Exists(slotPath) is false)
            {
                // The only window this can fire in is between the two renames. Putting it back is
                // worth attempting because the alternative is a slot that vanished.
                Directory.Move(displaced, slotPath);
            }

            throw;
        }
        finally
        {
            // What the caller already decided to displace. A failure here leaves a dotted folder
            // beside the slots that no adapter enumerates - untidy, and not worth failing a
            // successful unpack over.
            TryDeleteDirectory(displaced);
        }
    }


    /// <summary>
    /// Writes the slot as a zip into <paramref name="destination"/> and returns the SHA-256 of
    /// exactly the bytes it wrote.
    /// </summary>
    /// <remarks>
    /// The stream is wrapped so that hashing sees the archive as it is produced, one pass, which is
    /// what lets <see cref="HashSlotAsync"/> point it at <see cref="Stream.Null"/> and pay nothing.
    /// The wrapper reports itself unseekable on purpose: <see cref="ZipArchive"/> patches local
    /// headers after the fact when it can seek, and a hash taken while writing would then not be the
    /// hash of the finished file. Refusing to seek makes the two the same by construction, at the
    /// cost of a data descriptor per entry.
    /// </remarks>
    private static async Task<string> WriteArchiveAsync(
        IInstanceSavegameAdapter adapter,
        SavegameSlotId slot,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var slotPath = Path.GetFullPath(adapter.GetSlotPath(slot));

        await using var hashing = new HashingStream(destination);

        // leaveOpen, so the archive's central directory has been written and counted before the hash
        // is read - and so disposing the archive does not dispose the caller's destination.
        using (var archive = new ZipArchive(hashing, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (relativePath, fullPath) in EnumerateContents(adapter, slotPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                entry.LastWriteTime = _fixedTimestamp;

                // Shared for write and delete: the game may hold the save open, and a hash taken for
                // a drift check must never be the reason it fails to save.
                await using var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var target = entry.Open();

                await source.CopyToAsync(target, cancellationToken);
            }
        }

        return hashing.GetHash();
    }

    /// <summary>
    /// Every file under the slot that belongs in a packed save, as (archive name, path on disk),
    /// ordinal by archive name.
    /// </summary>
    /// <remarks>
    /// Materialised rather than lazy, and sorted before anything is written, because the order the
    /// filesystem hands files back is not a property of the save - it varies with the volume, with
    /// fragmentation, and with the order somebody's game happened to write them. The relative path is
    /// forward-slashed before the adapter is asked about it, so an adapter sees the same string the
    /// archive will store.
    /// </remarks>
    private static IReadOnlyList<(string RelativePath, string FullPath)> EnumerateContents(IInstanceSavegameAdapter adapter, string slotPath)
    {
        if (Directory.Exists(slotPath) is false)
        {
            return [];
        }

        return
        [
            .. new DirectoryInfo(slotPath)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(x => (RelativePath: ToArchivePath(Path.GetRelativePath(slotPath, x.FullName)), x.FullName))
                .Where(x => adapter.BelongsInPackedSave(x.RelativePath))
                .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
        ];
    }

    private static async Task ExtractAsync(string archivePath, string root, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = ResolveWithin(root, entry.FullName);

            if (destination.HasValue is false)
            {
                // Zip slip. An archive is bytes from the server, and the day one of them says
                // '../../mods/x.zip' this is the line that decides whether unpacking a save can
                // write outside the slot it names.
                throw new InvalidDataException($"The archive entry '{entry.FullName}' would be written outside the savegame slot. Nothing was unpacked.");
            }

            // A name ending in a separator is a directory entry with no content. Nothing this packer
            // writes has one, but an archive is not required to have come from this packer.
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination.Value);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination.Value)!);

            await using var target = new FileStream(destination.Value, FileMode.Create, FileAccess.Write, FileShare.None, _bufferSize, FileOptions.Asynchronous);
            await using var source = entry.Open();

            await source.CopyToAsync(target, cancellationToken);
        }
    }

    /// <summary>
    /// Where an entry lands, or <c>None</c> where that is anywhere but inside
    /// <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// Rooted names are refused before combining, because <see cref="Path.Combine(string, string)"/>
    /// discards the root it is given when the second part is absolute - so an entry called
    /// <c>C:\Windows\...</c> would otherwise resolve to itself and pass a naive prefix check on the
    /// wrong string. Compared ordinally: both sides come from <see cref="Path.GetFullPath"/>, which
    /// resolves <c>..</c> without touching the casing of what it was given.
    /// </remarks>
    private static Maybe<string> ResolveWithin(string root, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName))
        {
            return Maybe<string>.None;
        }

        var full = Path.GetFullPath(Path.Combine(root, entryName));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.Ordinal)
            ? full
            : Maybe<string>.None;
    }

    private static string ToArchivePath(string relativePath)
        => relativePath.Replace(Path.DirectorySeparatorChar, '/');

    private static string GetTemporaryArchivePath()
        => Path.Combine(Path.GetTempPath(), "modsdude", "savegames", $"{Guid.NewGuid():N}.zip");

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            // A leftover temporary archive costs disk space until the machine's temp folder is swept.
            _log.LogDebug(exception, "Could not delete the temporary archive {File}.", path);
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception)
        {
            // Same bargain as the file above, one directory larger.
            _log.LogDebug(exception, "Could not delete the staging directory {Directory}.", path);
        }
    }


    /// <summary>
    /// A write-only stream that hashes everything on its way through and owns nothing.
    /// </summary>
    /// <remarks>
    /// <b>Never disposes what it wraps</b>, so the caller keeps control of the file it is writing to,
    /// and <b>never claims to seek</b> - see <see cref="WriteArchiveAsync"/> for why that is the
    /// whole point rather than a limitation.
    /// </remarks>
    private sealed class HashingStream(Stream inner) : Stream
    {
        private readonly IncrementalHash _digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _written;


        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;

        /// <summary>How much has gone through. Readable because a wrapper that cannot say is harder
        /// to debug; not settable, which is what unseekable means.</summary>
        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }


        /// <summary>The digest so far, in the encoding the repo records hashes in.</summary>
        public string GetHash() => ModContentHasher.Format(_digest.GetCurrentHash());

        public override void Write(byte[] buffer, int offset, int count)
            => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _digest.AppendData(buffer);
            inner.Write(buffer);
            _written += buffer.Length;
        }

        public override void WriteByte(byte value)
            => Write([value]);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _digest.AppendData(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
            _written += buffer.Length;
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _digest.Dispose();
            }

            // inner is deliberately left alone.
        }
    }
}
