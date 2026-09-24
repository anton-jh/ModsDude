using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.Concurrent;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// What this client last saw a savegame's head snapshot and claim to be, remembered so the drift
/// check can ask without going to the network.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>ProfileService</c> answering <see cref="Sync.IProfileRevisions"/>: it is
/// populated as a side effect of something having read a savegame list - the repo's Saves page, or
/// the claim watch reading the lists of whatever this machine holds - and it answers <c>null</c> for
/// everything else. Null is the honest answer and a deliberate one: the drift check itself never goes
/// to the network, because its whole point is that it works offline.
/// </para>
/// <para>
/// It is a cache of an observation, not a source of truth, so it is never persisted and never
/// repaired. A stale head can only under-report - a head that has moved since the list was read
/// reads as the older number, which says nothing rather than something wrong. A stale claim could
/// over-report, which is why taking one records it here at once: see <see cref="RecordOwnClaim"/>.
/// </para>
/// </remarks>
public sealed class SavegameSightingCache : ISavegameSightings
{
    private readonly ConcurrentDictionary<(Guid RepoId, Guid SavegameId), int> _heads = [];
    private readonly ConcurrentDictionary<(Guid RepoId, Guid SavegameId), SavegameClaimSighting> _claims = [];


    public int? GetHeadSnapshot(Guid repoId, Guid savegameId)
    {
        return _heads.TryGetValue((repoId, savegameId), out var head) ? head : null;
    }

    public SavegameClaimSighting? GetClaim(Guid repoId, Guid savegameId)
    {
        return _claims.TryGetValue((repoId, savegameId), out var claim) ? claim : null;
    }

    /// <summary>
    /// Records what a freshly read savegame list says. Called by whatever just read one; a savegame
    /// with no head yet - published in the same breath and not yet answered for - is skipped rather
    /// than recorded as zero.
    /// </summary>
    /// <param name="currentUserId">
    /// Who is signed in, which is what decides whether a claim is yours. Null where the caller could
    /// not find out, and then the claims are left as they were rather than all read as somebody else's.
    /// </param>
    public void Record(Guid repoId, IEnumerable<SavegameDto> savegames, string? currentUserId)
    {
        foreach (var savegame in savegames)
        {
            if (savegame.Head is not null)
            {
                _heads[(repoId, savegame.Id)] = savegame.Head.Number;
            }

            if (currentUserId is { Length: > 0 } me)
            {
                _claims[(repoId, savegame.Id)] = Sighting(savegame.Checkout, me);
            }
        }
    }

    /// <summary>
    /// Records the claim the signed-in user has just taken, straight from the check-out's answer.
    /// </summary>
    /// <remarks>
    /// Without this, the list read before the check-out - naming whoever it was taken from, or nobody -
    /// would stand until the next read, and a drift check in between would report this user's own
    /// check-out as their save having been taken over.
    /// </remarks>
    public void RecordOwnClaim(Guid repoId, Guid savegameId, SavegameCheckoutDto checkout)
    {
        _claims[(repoId, savegameId)] = new SavegameClaimSighting(Holder(checkout), IsYours: true);
    }


    private static SavegameClaimSighting Sighting(SavegameCheckoutDto? checkout, string currentUserId)
    {
        if (checkout is null || checkout.Status is SavegameCheckoutStatus.Ended)
        {
            return new SavegameClaimSighting(null, IsYours: false);
        }

        return new SavegameClaimSighting(Holder(checkout), checkout.User.Id == currentUserId);
    }

    private static SavegameClaimHolder Holder(SavegameCheckoutDto checkout)
        => new(checkout.User.Id, checkout.User.DisplayName, checkout.TakenAt);
}
