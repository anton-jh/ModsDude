using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Sync;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

public interface IProfileService : IUserScopedState, IProfileRevisions, IProfileRevisionComparer
{
    /// <summary>Raised for a profile that did not exist a moment ago, so the shell can navigate to it.</summary>
    event Action<Guid>? ProfileCreated;

    /// <summary>
    /// Raised when an existing profile's contents changed. The <see cref="ProfileDto"/> instance in
    /// <see cref="Profiles"/> is updated in place rather than replaced - replacing it would take the
    /// sidebar entry, and the selection on it, down with it - and the DTO cannot announce that
    /// itself.
    /// </summary>
    event Action<Guid>? ProfileUpdated;

    ObservableCollection<ProfileDto> Profiles { get; }

    /// <summary>
    /// The profile in <see cref="Profiles"/> with this id in this repo, or null where there is none -
    /// including an archived one, which <see cref="Profiles"/> does not hold.
    /// </summary>
    ProfileDto? FindLive(Guid repoId, Guid? profileId);

    /// <summary>
    /// Asks the server for one profile of any repo this account is in, archived ones included, or
    /// null where it does not exist.
    /// </summary>
    Task<ProfileDto?> FindProfile(Guid repoId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// Which repo <see cref="Profiles"/> was last refreshed for, or null before the first refresh.
    /// </summary>
    /// <remarks>
    /// Not something the list can say about itself: a repo with no profiles is an empty list, and so
    /// is a list nobody has filled yet.
    /// </remarks>
    Guid? HeldRepoId { get; }

    /// <summary>
    /// What the last background check found on the server that <see cref="Profiles"/> does not show
    /// yet, or null where it found nothing. About <see cref="HeldRepoId"/>, and cleared by any
    /// refresh, which is what brings it in.
    /// </summary>
    RemoteChanges? PendingChanges { get; }

    /// <summary>Raised when <see cref="PendingChanges"/> is set or cleared.</summary>
    event EventHandler? PendingChangesChanged;

    /// <summary>
    /// Asks the server whether the held repo's profiles have changed, and records the answer in
    /// <see cref="PendingChanges"/> without touching <see cref="Profiles"/>.
    /// </summary>
    /// <remarks>
    /// <b>Discarded where the list moved while the question was out</b> - a profile created, renamed or
    /// saved on this machine in the meantime, or the list handed to another repo. Comparing an answer
    /// from before that with a list from after it would report this client's own change as somebody
    /// else's.
    /// </remarks>
    Task CheckForChanges(CancellationToken cancellationToken);

    Task RefreshProfiles(Guid repoId, CancellationToken cancellationToken);

    /// <param name="copyFrom">
    /// A revision of another profile in the repo to branch off, or <c>null</c> for an empty profile.
    /// The new profile's first revision pins exactly what that one pinned.
    /// </param>
    Task CreateProfile(
        Guid repoId,
        string name,
        CopyProfileRevisionRequest? copyFrom = null,
        CancellationToken cancellationToken = default);

    Task UpdateProfile(Guid repoId, Guid profileId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Permanently deletes an archived profile. Refused by the server for one that is still live -
    /// deleting is reached from the Archive and nowhere else.
    /// </summary>
    Task DeleteProfile(Guid repoId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a profile away. It leaves the sidebar and gives up its name; everything else about it
    /// stays exactly as it was - see the server's <c>IArchivable</c>.
    /// </summary>
    Task ArchiveProfile(Guid repoId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings one back, optionally under a new name.
    /// </summary>
    /// <remarks>
    /// An archived profile gave up its name, so the one it wants back may since have been taken.
    /// That comes out of here as a <see cref="UserFriendlyException"/> the caller turns into the
    /// rename prompt - the clash is deferred to this moment precisely because it is the only one
    /// with somebody present to resolve it.
    /// </remarks>
    Task<ProfileDto> RestoreProfile(Guid repoId, Guid profileId, string? name, CancellationToken cancellationToken);

    /// <summary>
    /// The repo's archived profiles. Read on demand rather than held: the Archive is a page somebody
    /// visits, not a thing the shell is built from.
    /// </summary>
    Task<IReadOnlyList<ProfileDto>> GetArchivedProfiles(Guid repoId, CancellationToken cancellationToken);

    /// <summary>
    /// How many mods the profile pins and how big they are. Not held in <see cref="Profiles"/>: the DTO does
    /// not carry them.
    /// </summary>
    Task<ProfileModStatistics> GetModStatistics(Guid repoId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// The profile's history, newest first, with the number of the revision that is current.
    /// </summary>
    Task<ProfileHistory> GetHistory(Guid repoId, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts an older revision's mod list back by copying it to the front. Nothing is deleted, so the
    /// revisions in between stay readable and this is itself undoable.
    /// </summary>
    Task<ProfileRevisionDto> RestoreRevision(Guid repoId, Guid profileId, int number, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a save just minted this revision, so the cached head is the server's before anything
    /// reads it.
    /// </summary>
    /// <remarks>
    /// <see cref="IProfileRevisions.GetHeadRevision"/> is what the drift check compares a freshly applied folder against.
    /// A save applies and checks straight after writing the revision, so waiting for the page to catch
    /// up would compare revision N+1 on disk with an N that is no longer the head, and report a
    /// profile that has just been applied as drifted until the next window activation.
    /// </remarks>
    void NoteRevisionSaved(Guid profileId, int number);

    /// <summary>
    /// Deletes old revisions, which is how the mod versions they pin stop being undeletable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one thing on this service that destroys history rather than adding to it, and the head is
    /// refused by the server, so nothing here can change what the profile currently pins - which is
    /// why no cached <c>HeadRevision</c> needs touching afterwards.
    /// </para>
    /// <para>
    /// One request for the whole selection. It deletes what it can and names what it cannot, so a
    /// hundred revisions blocked by one savegame is an answer rather than an exercise in bisection.
    /// </para>
    /// </remarks>
    Task<PruneProfileRevisionsResponse> PruneRevisions(
        Guid repoId, Guid profileId, IReadOnlyList<int> revisions, CancellationToken cancellationToken);

    /// <summary>
    /// What the profile pins, with each version resolved to the registered record behind it - which
    /// is what the shared list row needs to render one.
    /// </summary>
    /// <remarks>
    /// Two reads and a join, and deliberately not a <c>ModCatalog</c>: the catalog exists to merge
    /// the repo's mods with what is on this machine's disks, and a reader who cannot edit the profile
    /// has no use for the local half and should not pay a scan for it. Both routes are readable at
    /// Guest, which is the level this is for.
    /// </remarks>
    /// <param name="revision">
    /// Which revision to read, or <c>null</c> for the profile's current one. An older revision is
    /// the same list rendered the same way - it is only read-only because nothing anywhere can write
    /// to one.
    /// </param>
    Task<IReadOnlyList<PinnedMod>> GetPinnedMods(Guid repoId, Guid profileId, int? revision, CancellationToken cancellationToken);
}
