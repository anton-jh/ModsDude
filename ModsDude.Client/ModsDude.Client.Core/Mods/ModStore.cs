using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Stores;

namespace ModsDude.Client.Core.Mods;

public sealed class ModStore(
    IModsClient modsClient,
    IStoreDispatcher dispatcher,
    ILogger<ModStore> logger)
    : IModStore
{
    private readonly StoreLoads<Guid> _loads = new(dispatcher, logger);
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Held> _held = [];


    public async Task<IReadOnlyList<ModDto>> GetAsync(Guid repoId, CancellationToken cancellationToken)
    {
        await _loads.ReadAsync(repoId, ct => ReadChangesAsync(repoId, ct), changes => Apply(repoId, changes), cancellationToken);

        lock (_lock)
        {
            return _held.TryGetValue(repoId, out var held)
                ? held.Versions
                : throw new OperationCanceledException("The user changed while the repo's mods were being read.");
        }
    }

    public bool IsLoaded(Guid repoId)
    {
        lock (_lock)
        {
            return _held.ContainsKey(repoId);
        }
    }

    public void ClearUserState()
    {
        _loads.Reset();

        lock (_lock)
        {
            _held.Clear();
        }
    }


    /// <summary>Every page of what changed since the last read, from the number that read ended at.</summary>
    private async Task<Changes> ReadChangesAsync(Guid repoId, CancellationToken cancellationToken)
    {
        long after;

        lock (_lock)
        {
            after = _held.TryGetValue(repoId, out var held) ? held.Sequence : 0;
        }

        var from = after;
        var mods = new List<ModDto>();
        var deleted = new List<ModVersionRefDto>();
        GetModsResponse page;

        do
        {
            page = await modsClient.GetModsV1Async(repoId, after, null, cancellationToken);

            mods.AddRange(page.Mods);
            deleted.AddRange(page.Deleted);
            after = page.Sequence;
        }
        while (page.HasMore);

        return new Changes(from, after, mods, deleted);
    }

    private void Apply(Guid repoId, Changes changes)
    {
        lock (_lock)
        {
            var held = _held.GetValueOrDefault(repoId) ?? Held.Empty;

            // Read from a number another read has moved past since: this answer is older than what is held.
            if (held.Sequence != changes.From)
            {
                return;
            }

            var byId = held.Versions.ToDictionary(Key);

            // Deletions first: a version deleted and registered again is listed in both, and exists.
            foreach (var version in changes.Deleted)
            {
                byId.Remove((version.ModId, version.VersionId));
            }

            foreach (var version in changes.Mods)
            {
                byId[Key(version)] = version;
            }

            _held[repoId] = new Held(
                changes.To,
                [.. byId.Values.OrderBy(x => x.ModId, StringComparer.Ordinal).ThenBy(x => x.VersionId, StringComparer.Ordinal)]);
        }
    }

    private static (string ModId, string VersionId) Key(ModDto version) => (version.ModId, version.VersionId);


    private sealed record Held(long Sequence, IReadOnlyList<ModDto> Versions)
    {
        public static Held Empty { get; } = new(0, []);
    }

    private sealed record Changes(long From, long To, IReadOnlyList<ModDto> Mods, IReadOnlyList<ModVersionRefDto> Deleted);
}
