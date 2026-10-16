using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.RemoteSources;

namespace ModsDude.Client.Core.Models;

/// <summary>A version a remote source offers, and the source it is downloaded through.</summary>
/// <param name="Note">What an earlier download of this link turned out to hold, where one was made.</param>
public sealed record RemoteOffer(IRemoteModSource Source, RemoteModVersion Version, RemoteDownloadNote? Note = null)
{
    public ModSourceId SourceId => ModSourceId.ForRemote(Source.Key);

    public string SourceName => Source.DisplayName;

    /// <summary>An earlier download of this link held something other than the version offered.</summary>
    public bool IsBroken => Note is not null && Note.Held != Version.Identity;

    /// <summary>The source as an occurrence's source, for a file downloaded from it into <paramref name="folder"/>.</summary>
    public ModSource AsSource(string folder) => new(SourceId, SourceName, folder, ModSourceKind.Remote);
}
