using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Stores;
using ModsDude.Client.Core.Users;
using System.Text.Json;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameStore(
    ISavegamesClient savegamesClient,
    ICurrentUserStore currentUser,
    IStoreDispatcher dispatcher,
    ILogger<SavegameStore> logger)
    : ISavegameStore
{
    private readonly StoreLoads<Guid> _liveLoads = new(dispatcher, logger);
    private readonly StoreLoads<Guid> _archivedLoads = new(dispatcher, logger);
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, IReadOnlyList<SavegameDto>> _live = [];
    private readonly Dictionary<Guid, IReadOnlyList<SavegameDto>> _archived = [];


    public event Action<Guid>? Changed;


    public IReadOnlyList<Guid> LoadedRepos
    {
        get
        {
            lock (_lock)
            {
                return [.. _live.Keys.Order()];
            }
        }
    }


    public IReadOnlyList<SavegameDto> Live(Guid repoId)
    {
        lock (_lock)
        {
            return _live.GetValueOrDefault(repoId) ?? [];
        }
    }

    public IReadOnlyList<SavegameDto> Archived(Guid repoId)
    {
        lock (_lock)
        {
            return _archived.GetValueOrDefault(repoId) ?? [];
        }
    }

    public SavegameDto? Find(Guid repoId, Guid savegameId)
        => Live(repoId).FirstOrDefault(x => x.Id == savegameId);

    public Task EnsureLoadedAsync(Guid repoId, CancellationToken cancellationToken)
        => IsHeld(_live, repoId) ? Task.CompletedTask : ReadLiveAsync(repoId, cancellationToken);

    public Task EnsureArchivedLoadedAsync(Guid repoId, CancellationToken cancellationToken)
        => IsHeld(_archived, repoId) ? Task.CompletedTask : ReadArchivedAsync(repoId, cancellationToken);

    public async Task RefreshAsync(Guid repoId, CancellationToken cancellationToken)
    {
        await ReadLiveAsync(repoId, cancellationToken);

        if (IsHeld(_archived, repoId))
        {
            await ReadArchivedAsync(repoId, cancellationToken);
        }
    }

    public Task RefreshArchivedAsync(Guid repoId, CancellationToken cancellationToken)
        => ReadArchivedAsync(repoId, cancellationToken);

    public async Task<T> WriteAsync<T>(Guid repoId, Func<CancellationToken, Task<T>> send, CancellationToken cancellationToken)
    {
        // Marked as a write on both lists, so a read that was out while it was sent is read again
        // rather than landing on top of it.
        T answer;

        try
        {
            answer = await _liveLoads.WriteAsync(repoId, send, _ => { }, cancellationToken);
            await _archivedLoads.ApplyAsync(repoId, () => { });
        }
        catch (ApiException<CustomProblemDetails>)
        {
            // The server refused it, which is the usual sign that what the caller saw has moved on.
            await RefreshAfterWriteAsync(repoId);

            throw;
        }

        await RefreshAfterWriteAsync(repoId);

        return answer;
    }

    /// <summary>
    /// Reads the repo again after a write, never failing: where the write happened, a caller told it
    /// failed would skip the local half of what it just did. A read that does not get through is the
    /// next read'"'"'s to catch up.
    /// </summary>
    private async Task RefreshAfterWriteAsync(Guid repoId)
    {
        try
        {
            await RefreshAsync(repoId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read the savegames of repo {RepoId} after writing to one of them.", repoId);
        }
    }

    public Task WriteAsync(Guid repoId, Func<CancellationToken, Task> send, CancellationToken cancellationToken)
        => WriteAsync(
            repoId,
            async ct =>
            {
                await send(ct);

                return true;
            },
            cancellationToken);

    public int? GetHeadSnapshot(Guid repoId, Guid savegameId)
        => Find(repoId, savegameId)?.Head?.Number;

    public SavegameClaimSighting? GetClaim(Guid repoId, Guid savegameId)
    {
        // Whose a claim is decides everything the drift check says about it, so without the user there
        // is no answer rather than one that reads every claim as somebody else's.
        if (Find(repoId, savegameId) is not SavegameDto savegame || currentUser.User is not CurrentUserDto me)
        {
            return null;
        }

        return savegame.Checkout is SavegameCheckoutDto checkout && checkout.Status is not SavegameCheckoutStatus.Ended
            ? new SavegameClaimSighting(Holder(checkout), checkout.User.Id == me.Id)
            : new SavegameClaimSighting(null, IsYours: false);
    }

    /// <remarks>
    /// Folded into the list held rather than waiting for the next read, so a drift check in between
    /// does not report this user's own check-out as their save being taken over.
    /// </remarks>
    public void RecordOwnClaim(Guid repoId, Guid savegameId, SavegameCheckoutDto checkout)
    {
        lock (_lock)
        {
            if (_live.TryGetValue(repoId, out var live))
            {
                _live[repoId] = [.. live.Select(x => x.Id == savegameId ? x with { Checkout = checkout } : x)];
            }
        }
    }

    public void ClearUserState()
    {
        _liveLoads.Reset();
        _archivedLoads.Reset();

        List<Guid> cleared;

        lock (_lock)
        {
            cleared = [.. _live.Keys.Union(_archived.Keys).Order()];
            _live.Clear();
            _archived.Clear();
        }

        foreach (var repoId in cleared)
        {
            Changed?.Invoke(repoId);
        }
    }


    private Task ReadLiveAsync(Guid repoId, CancellationToken cancellationToken)
        => _liveLoads.ReadAsync(
            repoId,
            ct => savegamesClient.GetSavegamesV1Async(repoId, ct),
            savegames => Hold(_live, repoId, savegames),
            cancellationToken);

    private Task ReadArchivedAsync(Guid repoId, CancellationToken cancellationToken)
        => _archivedLoads.ReadAsync(
            repoId,
            ct => savegamesClient.GetArchivedSavegamesV1Async(repoId, ct),
            savegames => Hold(_archived, repoId, savegames),
            cancellationToken);

    /// <remarks>
    /// Compared as JSON, because the generated records hold collections, which compare by reference:
    /// two reads of an unchanged list would otherwise always differ, and every read would announce a
    /// change.
    /// </remarks>
    private void Hold(Dictionary<Guid, IReadOnlyList<SavegameDto>> lists, Guid repoId, ICollection<SavegameDto> savegames)
    {
        IReadOnlyList<SavegameDto> ordered = [.. savegames.OrderBy(x => x.Id)];

        lock (_lock)
        {
            if (lists.TryGetValue(repoId, out var held) && JsonSerializer.Serialize(held) == JsonSerializer.Serialize(ordered))
            {
                return;
            }

            lists[repoId] = ordered;
        }

        Changed?.Invoke(repoId);
    }

    private bool IsHeld(Dictionary<Guid, IReadOnlyList<SavegameDto>> lists, Guid repoId)
    {
        lock (_lock)
        {
            return lists.ContainsKey(repoId);
        }
    }

    private static SavegameClaimHolder Holder(SavegameCheckoutDto checkout)
        => new(checkout.User.Id, checkout.User.DisplayName, checkout.TakenAt);
}
