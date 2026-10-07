using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// The live profiles of every repo this client has read, one list per repo.
/// </summary>
public interface IProfileStore : IUserScopedState, IProfileRevisions
{
    /// <summary>Raised for a profile that did not exist a moment ago, so the shell can navigate to it.</summary>
    event Action<Profile>? ProfileCreated;

    /// <summary>
    /// Raised on the store's thread with the repo whose list, or one of whose profiles, just changed.
    /// </summary>
    event Action<Guid>? Changed;

    /// <summary>
    /// The repo's live profiles. The same collection for as long as the user stays signed in, empty
    /// until the repo has been read.
    /// </summary>
    ObservableCollection<Profile> Live(Guid repoId);

    /// <summary>Whether the repo's profiles have been read at least once.</summary>
    bool IsLoaded(Guid repoId);

    /// <summary>Every repo whose profiles have been read.</summary>
    IReadOnlyList<Guid> LoadedRepos { get; }

    /// <summary>
    /// The live profile with this id in this repo, or null where there is none - including an archived
    /// one, and any in a repo not read yet. Safe from any thread.
    /// </summary>
    Profile? Find(Guid repoId, Guid? profileId);

    /// <summary>Reads the repo's profiles where they have not been read yet.</summary>
    Task EnsureLoadedAsync(Guid repoId, CancellationToken cancellationToken);

    Task RefreshAsync(Guid repoId, CancellationToken cancellationToken);

    /// <param name="copyFrom">
    /// A revision of another profile in the repo to branch off, or <c>null</c> for an empty profile.
    /// The new profile's first revision pins exactly what that one pinned.
    /// </param>
    Task<Profile> CreateAsync(Guid repoId, string name, CopyProfileRevisionRequest? copyFrom, CancellationToken cancellationToken);

    Task RenameAsync(Profile profile, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a profile away. It leaves the live list and gives up its name; everything else about it
    /// stays exactly as it was.
    /// </summary>
    Task ArchiveAsync(Profile profile, CancellationToken cancellationToken);

    /// <summary>Brings an archived profile back, optionally under a new name.</summary>
    /// <remarks>
    /// An archived profile gave up its name, so the one it wants back may since have been taken.
    /// That comes out of here as a <see cref="Exceptions.UserFriendlyException"/> the caller turns
    /// into the rename prompt.
    /// </remarks>
    Task<Profile> RestoreAsync(Guid repoId, Guid profileId, string? name, CancellationToken cancellationToken);

    /// <summary>Permanently deletes an archived profile. Refused by the server for one that is still live.</summary>
    Task DeleteAsync(Guid repoId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts an older revision's mod list back by copying it to the front. Nothing is deleted, so the
    /// revisions in between stay readable and this is itself undoable.
    /// </summary>
    Task<ProfileRevisionDto> RestoreRevisionAsync(Profile profile, int number, CancellationToken cancellationToken);

    /// <summary>Records the revision a save just wrote as the profile's newest.</summary>
    Task ApplyRevisionSavedAsync(Guid repoId, Guid profileId, int number);
}
