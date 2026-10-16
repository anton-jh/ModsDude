using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.RemoteSources;

/// <summary>
/// Downloads versions remote sources offer, into a folder only ModsDude uses. Nothing is kept there
/// beyond the run that downloaded it.
/// </summary>
public interface IRemoteModDownloader
{
    /// <summary>Starts a set of downloads. Disposing the run deletes everything it downloaded.</summary>
    IRemoteDownloadRun BeginRun();

    /// <summary>Deletes whatever no running download owns, such as what a crashed run left behind.</summary>
    void Sweep();
}

public interface IRemoteDownloadRun : IDisposable
{
    /// <summary>
    /// Downloads one offer into a folder of its own and returns that folder, which then holds exactly
    /// the archive, named as the source names it.
    /// </summary>
    /// <exception cref="RemoteDownloadException">The source answered with something that is not the archive.</exception>
    Task<string> DownloadAsync(RemoteOffer offer, IProgress<RemoteDownloadProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Deletes one download's folder, once its file has been imported or turned down.</summary>
    void Discard(string folder);
}

/// <param name="TotalBytes">Null where the source did not say how big the file is.</param>
public readonly record struct RemoteDownloadProgress(long BytesReceived, long? TotalBytes);
