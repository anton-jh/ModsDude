using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.Savegames;

/// <remarks>
/// A game holds savegames from any repo the user plays it with, so a name is read from the repo its
/// binding names. The archived list is read only for what the live one does not have, since
/// archiving does not release a hold.
/// </remarks>
public sealed class HeldSavegameNames(
    ISavegameBindingStore bindingStore,
    ISavegamesClient savegamesClient,
    IRepoStore repos,
    ILogger<HeldSavegameNames> logger) : IHeldSavegameNames
{
    public async Task<IReadOnlyDictionary<Guid, HeldSavegameName>> ReadAsync(
        Game game,
        Guid currentRepoId,
        IReadOnlyCollection<SavegameDto> known,
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

            if (repoId == currentRepoId)
            {
                Add(names, known, wanted, otherRepoName);
            }

            if (wanted.Count > 0)
            {
                Add(names, await TryReadAsync(repoId, archived: false, cancellationToken), wanted, otherRepoName);
            }

            if (wanted.Count > 0)
            {
                Add(names, await TryReadAsync(repoId, archived: true, cancellationToken), wanted, otherRepoName);
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
    /// A repo's savegames, or none where they cannot be read - access lost, the repo gone, the server
    /// unreachable. A name is only ever wording, so the savegame is then referred to without it.
    /// </summary>
    private async Task<IEnumerable<SavegameDto>> TryReadAsync(Guid repoId, bool archived, CancellationToken cancellationToken)
    {
        try
        {
            return archived
                ? await savegamesClient.GetArchivedSavegamesV1Async(repoId, cancellationToken)
                : await savegamesClient.GetSavegamesV1Async(repoId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not read the {Which} savegames of repo {RepoId} to name the savegames held from it.",
                archived ? "archived" : "live",
                repoId);

            return [];
        }
    }
}
