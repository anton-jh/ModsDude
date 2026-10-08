using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// A game holds savegames from any repo the user plays it with, so a name is read from the repo its
/// binding names. The archived list is read only for what the live one does not have, since
/// archiving does not release a hold.
/// </remarks>
public sealed class HeldSavegameNames(
    ISavegameBindingStore bindingStore,
    ISavegameStore savegames,
    IRepoStore repos,
    ILogger<HeldSavegameNames> logger) : IHeldSavegameNames
{
    public async Task<IReadOnlyDictionary<Guid, HeldSavegameName>> ReadAsync(
        Game game,
        Guid? currentRepoId,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, HeldSavegameName>();

        foreach (var held in bindingStore.GetBindings(game.Identity).GroupBy(x => x.RepoId).OrderBy(x => x.Key))
        {
            var repoId = held.Key;
            var otherRepoName = repoId == currentRepoId
                ? null
                : repos.Repos.FirstOrDefault(x => x.Id == repoId)?.Name ?? "another repo";

            var wanted = held.Select(x => x.SavegameId).ToHashSet();

            await TryReadAsync(repoId, savegames.EnsureLoadedAsync, cancellationToken);
            Add(names, savegames.Live(repoId), wanted, otherRepoName);

            if (wanted.Count > 0)
            {
                await TryReadAsync(repoId, savegames.EnsureArchivedLoadedAsync, cancellationToken);
                Add(names, savegames.Archived(repoId), wanted, otherRepoName);
            }
        }

        return names;
    }

    private static void Add(
        Dictionary<Guid, HeldSavegameName> names,
        IEnumerable<SavegameDto> savegames,
        HashSet<Guid> wanted,
        string? otherRepoName)
    {
        foreach (var savegame in savegames.Where(x => wanted.Contains(x.Id)))
        {
            names[savegame.Id] = new HeldSavegameName(savegame.Name, otherRepoName);
            wanted.Remove(savegame.Id);
        }
    }

    /// <summary>
    /// Reads one of a repo's lists, or leaves it unread where it cannot be - access lost, the repo gone,
    /// the server unreachable. A name is only ever wording, so the savegame is then referred to without it.
    /// </summary>
    private async Task TryReadAsync(Guid repoId, Func<Guid, CancellationToken, Task> read, CancellationToken cancellationToken)
    {
        try
        {
            await read(repoId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not read the savegames of repo {RepoId} to name the savegames held from it.", repoId);
        }
    }
}
