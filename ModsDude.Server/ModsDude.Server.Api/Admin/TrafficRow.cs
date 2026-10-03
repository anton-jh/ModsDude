using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Admin;

public record TrafficRow(TransferredFile File, int Downloads, long DownloadedBytes, int Uploads, long UploadedBytes)
{
    /// <summary>One row per kind of file, whether or not any of it moved.</summary>
    public static IReadOnlyList<TrafficRow> PerFile(IEnumerable<FileTransferTotal> totals)
    {
        var all = totals.ToList();

        return [.. Enum.GetValues<TransferredFile>()
            .Select(file =>
            {
                var downloads = all.Where(x => x.File == file && x.Direction is TransferDirection.Download).ToList();
                var uploads = all.Where(x => x.File == file && x.Direction is TransferDirection.Upload).ToList();

                return new TrafficRow(
                    file,
                    downloads.Sum(x => x.Count),
                    downloads.Sum(x => x.Bytes),
                    uploads.Sum(x => x.Count),
                    uploads.Sum(x => x.Bytes));
            })];
    }

    public static long DownloadedBytesOf(IEnumerable<FileTransferTotal> totals)
    {
        return totals.Where(x => x.Direction is TransferDirection.Download).Sum(x => x.Bytes);
    }

    public static long UploadedBytesOf(IEnumerable<FileTransferTotal> totals)
    {
        return totals.Where(x => x.Direction is TransferDirection.Upload).Sum(x => x.Bytes);
    }
}
