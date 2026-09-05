using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.Concurrent;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// What this client last saw a savegame's head version to be, remembered so the drift check can ask
/// without going to the network.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>ProfileService</c> answering <see cref="Sync.IProfileRevisions"/>: it is
/// populated as a side effect of a page having loaded a savegame list, and it answers <c>null</c> for
/// everything else. Null is the honest answer and a deliberate one - "somebody took this over and
/// checked in" then simply goes unreported for a repo nobody has opened, rather than costing a round
/// trip per held save on every window activation, in a check whose whole point is that it works
/// offline.
/// </para>
/// <para>
/// It is a cache of an observation, not a source of truth, so it is never persisted, never
/// invalidated, and never repaired. A stale entry can only under-report - a head that has moved since
/// the list was read reads as the older number, which says nothing rather than something wrong - and
/// the next time the list is opened it corrects itself.
/// </para>
/// </remarks>
public sealed class SavegameHeadVersionCache : ISavegameHeadVersions
{
    private readonly ConcurrentDictionary<(Guid RepoId, Guid SavegameId), int> _heads = [];


    public int? GetHeadVersion(Guid repoId, Guid savegameId)
    {
        return _heads.TryGetValue((repoId, savegameId), out var head) ? head : null;
    }

    /// <summary>
    /// Records what a freshly read savegame list says. Called by whatever just read one; a savegame
    /// with no head yet - published in the same breath and not yet answered for - is skipped rather
    /// than recorded as zero.
    /// </summary>
    public void Record(Guid repoId, IEnumerable<SavegameDto> savegames)
    {
        foreach (var savegame in savegames)
        {
            if (savegame.Head is null)
            {
                continue;
            }

            _heads[(repoId, savegame.Id)] = savegame.Head.Number;
        }
    }
}
