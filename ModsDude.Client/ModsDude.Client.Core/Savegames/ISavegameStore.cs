using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// The savegames of every repo this client has read, live and archived, one list of each per repo.
/// </summary>
/// <remarks>
/// Also what the drift check asks about a held savegame's head and claim, through
/// <see cref="ISavegameSightings"/>: the answer is there for every repo read so far.
/// </remarks>
public interface ISavegameStore : IUserScopedState, ISavegameSightings
{
    /// <summary>Raised on the store's thread with the repo whose live or archived savegames just changed.</summary>
    event Action<Guid>? Changed;

    /// <summary>The repo's live savegames, empty until the repo has been read. Ordered by id.</summary>
    IReadOnlyList<SavegameDto> Live(Guid repoId);

    /// <summary>The repo's archived savegames, empty until they have been read. Ordered by id.</summary>
    IReadOnlyList<SavegameDto> Archived(Guid repoId);

    /// <summary>Every repo whose live savegames have been read.</summary>
    IReadOnlyList<Guid> LoadedRepos { get; }

    /// <summary>A live savegame, or null where there is none or the repo has not been read.</summary>
    SavegameDto? Find(Guid repoId, Guid savegameId);

    /// <summary>Reads the repo's live savegames where they have not been read yet.</summary>
    Task EnsureLoadedAsync(Guid repoId, CancellationToken cancellationToken);

    /// <summary>Reads the repo's archived savegames where they have not been read yet.</summary>
    Task EnsureArchivedLoadedAsync(Guid repoId, CancellationToken cancellationToken);

    /// <summary>Reads the repo's live savegames again, and its archived ones where those are held.</summary>
    Task RefreshAsync(Guid repoId, CancellationToken cancellationToken);

    /// <summary>Reads the repo's archived savegames again.</summary>
    Task RefreshArchivedAsync(Guid repoId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a write to one of the repo's savegames, then reads the repo again - also where the server
    /// refused it, which usually means the list moved - so nothing reads the list from before it.
    /// </summary>
    /// <returns>The server's answer.</returns>
    Task<T> WriteAsync<T>(Guid repoId, Func<CancellationToken, Task<T>> send, CancellationToken cancellationToken);

    /// <inheritdoc cref="WriteAsync{T}"/>
    Task WriteAsync(Guid repoId, Func<CancellationToken, Task> send, CancellationToken cancellationToken);
}
