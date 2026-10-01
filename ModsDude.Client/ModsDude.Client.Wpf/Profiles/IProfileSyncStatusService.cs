using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Profiles;

public interface IProfileSyncStatusService
{
    /// <summary>Raised on the UI thread after anything that can change an answer from here.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Which profile of this repo the game follows, or null where it follows another repo's or none -
    /// or where no game is connected, which is the same answer.
    /// </summary>
    Guid? ActiveProfileOf(Repo repo);

    /// <summary>The state of whichever profile of this repo the game follows.</summary>
    ProfileSyncState StateOf(Repo repo);

    ProfileSyncState StateOf(Repo repo, Guid profileId);
}
