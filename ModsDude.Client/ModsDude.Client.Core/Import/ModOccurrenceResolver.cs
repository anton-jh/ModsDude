using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Import;

/// <summary>
/// One distinct file behind a (mod, version), and everywhere on this machine it was found.
/// </summary>
/// <param name="ContentHash">
/// Null for a version that turned up in exactly one source, where nothing had to be compared and
/// hashing an archive would have bought nothing.
/// </param>
public sealed record ModFileCandidate(
    string? ContentHash,
    long FileLength,
    IReadOnlyList<ModOccurrence> Occurrences)
{
    /// <summary>
    /// The occurrence the import reads from. Any of them would do - they are byte-identical - so it
    /// is the first, which keeps the choice stable between scans.
    /// </summary>
    public ModOccurrence Primary => Occurrences[0];

    /// <summary>
    /// How this candidate is named in a dialog's answer. The hash where there is one; otherwise the
    /// length, which is enough because the only candidate without a hash is the only candidate.
    /// </summary>
    public string Key => ContentHash ?? $"length:{FileLength}";
}

/// <summary>
/// Works out how many genuinely different files claim to be one mod version.
/// </summary>
/// <remarks>
/// <para>
/// <b>By content, not by size.</b> The catalog's own conflict chip compares file lengths, which is
/// free and runs over every row of every scan - but equal lengths are not equal bytes, so it
/// under-reports, and that is the wrong way round for a decision that ends with somebody's file in
/// the Recycle Bin. Everything here hashes.
/// </para>
/// <para>
/// <b>Only where there is something to compare.</b> A version found in one source is one candidate
/// and no archive is read. The cost therefore falls on duplicates alone, and only on the ones the
/// user actually selected for import - which is also after the versions the repo already holds have
/// been dropped, so re-importing an installed folder pays nothing.
/// </para>
/// <para>
/// Length is not used as a shortcut past hashing even though a difference in it is conclusive. The
/// saving would land only on the rare case where two sources genuinely disagree, and it would leave
/// candidates that have no hash sitting beside candidates that do - two kinds of identity for the
/// dialog and its answer to keep straight, in exchange for not reading a file the user is about to
/// be asked about anyway.
/// </para>
/// </remarks>
public static class ModOccurrenceResolver
{
    /// <summary>
    /// The distinct files behind one version, most-recently-written first. Copies are hashed in
    /// parallel, since they usually sit on different folders and often on different disks.
    /// </summary>
    /// <param name="bytesHashed">Told how many bytes have been hashed so far, across every copy.</param>
    /// <returns>
    /// Empty where nothing could be read. An occurrence whose file has gone or cannot be opened is
    /// dropped rather than failing the version: the other copy of it is very often still there, and
    /// that is the copy the import wanted.
    /// </returns>
    public static async Task<IReadOnlyList<ModFileCandidate>> ResolveAsync(
        IReadOnlyList<ModOccurrence> occurrences,
        IProgress<long>? bytesHashed,
        CancellationToken cancellationToken)
    {
        if (occurrences.Count == 0)
        {
            return [];
        }

        if (occurrences.Count == 1)
        {
            return [new ModFileCandidate(null, occurrences[0].FileLength, occurrences)];
        }

        var hashes = new string?[occurrences.Count];
        var counted = new long[occurrences.Count];
        long total = 0;

        await Parallel.ForAsync(0, occurrences.Count, cancellationToken, async (i, ct) =>
        {
            var progress = bytesHashed is null
                ? null
                : new InlineProgress<long>(read =>
                {
                    // Under the lock, so what is reported only ever grows.
                    lock (counted)
                    {
                        total += read - counted[i];
                        counted[i] = read;
                        bytesHashed.Report(total);
                    }
                });

            hashes[i] = await TryHashAsync(occurrences[i], progress, ct);
        });

        // Indexed rather than collected, so the copies keep the order they were found in.
        var hashed = occurrences
            .Select((occurrence, i) => (Hash: hashes[i], Occurrence: occurrence))
            .Where(x => x.Hash is not null)
            .Select(x => (Hash: x.Hash!, x.Occurrence));

        return
        [
            .. hashed
                .GroupBy(x => x.Hash, StringComparer.OrdinalIgnoreCase)
                .Select(x => new ModFileCandidate(
                    x.Key,
                    x.First().Occurrence.FileLength,
                    [.. x.Select(y => y.Occurrence)]))
                // Newest first, so the choice a dialog offers first is the copy most recently put
                // there - which is the one somebody just downloaded, far more often than not.
                .OrderByDescending(LastWritten)
                .ThenBy(x => x.ContentHash, StringComparer.Ordinal)
        ];
    }


    /// <summary>
    /// When the newest of a candidate's copies was written, or <see cref="DateTime.MinValue"/> where
    /// none of them will say. Only ever an ordering hint, never a decision.
    /// </summary>
    public static DateTime LastWritten(ModFileCandidate candidate)
    {
        var newest = DateTime.MinValue;

        foreach (var occurrence in candidate.Occurrences)
        {
            try
            {
                var written = File.GetLastWriteTimeUtc(occurrence.FilePath);

                if (written > newest)
                {
                    newest = written;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A file that will not say when it was written still sorts, just last.
            }
        }

        return newest;
    }

    private static async Task<string?> TryHashAsync(ModOccurrence occurrence, IProgress<long>? bytesRead, CancellationToken cancellationToken)
    {
        try
        {
            using var content = occurrence.OpenStream();

            return await ModContentHasher.ComputeAsync(content, bytesRead, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
