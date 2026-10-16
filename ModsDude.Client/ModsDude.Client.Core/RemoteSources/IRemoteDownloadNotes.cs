using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.RemoteSources;

/// <summary>
/// What each downloaded link turned out to hold, so an offer whose file is something else is not
/// offered, or downloaded, as what it claims.
/// </summary>
public interface IRemoteDownloadNotes
{
    RemoteDownloadNote? Find(Uri url);

    void Record(RemoteDownloadNote note);
}

/// <param name="Held">What the file read as, or null where it was not a mod.</param>
public sealed record RemoteDownloadNote(Uri Url, ModVersionIdentity Offered, ModVersionIdentity? Held);
