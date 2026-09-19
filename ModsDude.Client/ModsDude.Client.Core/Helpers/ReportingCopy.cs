using System.Buffers;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// <see cref="Stream.CopyToAsync(Stream, CancellationToken)"/> that says how much it has moved.
/// </summary>
internal static class ReportingCopy
{
    private const int BufferSize = 64 * 1024;

    /// <summary>How much has to move before another report. The reads are 64 KiB; nothing wants that many messages.</summary>
    private const int ReportEvery = 512 * 1024;


    /// <param name="copied">
    /// Called with the bytes moved since its last call, at most once per <see cref="ReportEvery"/> and
    /// once more at the end. Null copies without counting.
    /// </param>
    public static async Task CopyAsync(Stream source, Stream destination, Action<long>? copied, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            long pending = 0;
            int read;

            while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                pending += read;

                if (copied is not null && pending >= ReportEvery)
                {
                    copied(pending);
                    pending = 0;
                }
            }

            if (copied is not null && pending > 0)
            {
                copied(pending);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
