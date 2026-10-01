using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

public interface IRepoRepository : IUserScopedState
{
    /// <summary>
    /// Raised for a repo that did not exist a moment ago, so the shell can navigate to it. Renames
    /// need no equivalent: the model is updated in place and the menu entry follows it.
    /// </summary>
    event Action<Guid>? RepoCreated;

    ObservableCollection<Repo> Repos { get; }

    /// <summary>
    /// Whether this account's repos have been read at least once.
    /// </summary>
    /// <remarks>
    /// <b>An empty list means two different things and something has to tell them apart.</b> Before
    /// the first read it means "not asked yet"; after one it means this account is in no repos. The
    /// drift notice is the caller that cares: a game whose repo it cannot find is either a shell
    /// that started three seconds ago or a game this account can do nothing about, and those get
    /// opposite sentences. Set on success only - a failed read has established nothing.
    /// </remarks>
    bool HasLoaded { get; }

    /// <summary>
    /// What the last background check found on the server that <see cref="Repos"/> does not show
    /// yet, or null where it found nothing. Cleared by any refresh, which is what brings it in.
    /// </summary>
    RemoteChanges? PendingChanges { get; }

    /// <summary>Raised when <see cref="PendingChanges"/> is set or cleared.</summary>
    event EventHandler? PendingChangesChanged;

    /// <summary>
    /// Asks the server whether the list has changed, and records the answer in
    /// <see cref="PendingChanges"/> without touching <see cref="Repos"/>.
    /// </summary>
    /// <remarks>
    /// <b>Discarded where the list moved while the question was out.</b> Creating, joining or
    /// renaming a repo on this machine changes the list between the request and the answer, and
    /// comparing an answer from before that with a list from after it would report this client's
    /// own change as somebody else's.
    /// </remarks>
    Task CheckForChanges(CancellationToken cancellationToken);

    Task RefreshRepos(CancellationToken cancellationToken);

    Task CreateRepo(string name, string adapterId, DynamicForm baseSettings, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a repo the user has just joined into the list, so the shell can navigate to it without
    /// waiting for a refresh. Ignored where the repo is already there - redeeming a code twice is
    /// allowed, and must not produce two of the same repo.
    /// </summary>
    void AddJoinedRepo(RepoMembershipDto membership);

    Task Update(Repo repo, string name, DynamicForm baseSettings, CancellationToken cancellationToken);

    /// <summary>
    /// Permanently deletes an archived repo. Refused by the server for one that is still live, and
    /// for one that still holds mods.
    /// </summary>
    Task DeleteRepo(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a repo away, for everybody. Archiving is repo state rather than membership state, so
    /// this is not a personal "hide it from me" - it leaves every member's sidebar at once.
    /// </summary>
    Task ArchiveRepo(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Brings one back, under the name it went away with. Unlike restoring a profile or a savegame
    /// this takes no name and cannot fail on one: repo names are not unique, so an archived repo
    /// never gave its name up for anybody else to take.
    /// </summary>
    Task RestoreRepo(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The archived repos this user is a member of. Read on demand: the Archive is a page somebody
    /// visits, not part of the shell.
    /// </summary>
    Task<IReadOnlyList<RepoMembershipDto>> GetArchivedRepos(CancellationToken cancellationToken);
}
