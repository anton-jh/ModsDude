using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Savegames;

public sealed class HeldSavegameClaims(
    IDriftCandidateSource games,
    ISavegameBindingStore bindings,
    ISavegameStore store)
    : IHeldSavegameClaims
{
    public IReadOnlyList<Guid> Repos()
        => [.. Held().Select(x => x.RepoId).Distinct().Order()];

    public IReadOnlyList<HeldSavegameClaim> Capture()
        => [.. Held().Select(x => new HeldSavegameClaim(
            x.RepoId,
            x.SavegameId,
            store.GetHeadSnapshot(x.RepoId, x.SavegameId),
            store.GetClaim(x.RepoId, x.SavegameId)))];


    private IEnumerable<SavegameCheckoutBinding> Held()
        => games.GetDriftCandidates()
            .SelectMany(x => bindings.GetBindings(x.Identity))
            .OrderBy(x => x.RepoId)
            .ThenBy(x => x.SavegameId);
}
