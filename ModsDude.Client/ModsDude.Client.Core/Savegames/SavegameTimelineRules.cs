using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

public static class SavegameTimelineRules
{
    /// <summary>
    /// The ended claims whose ending a savegame's history shows as a row of its own.
    /// </summary>
    /// <remarks>
    /// A check-in that minted a snapshot and ended its claim in one request is already said by that
    /// snapshot, which carries the claim's id and the same moment. Every other ending gets a row: a
    /// check-in that changed nothing, a discard, a take-over, a check-in after earlier ones that kept
    /// playing, and one whose snapshot has since been pruned or is outside the loaded window.
    /// </remarks>
    public static IReadOnlyList<SavegameCheckoutDto> EndingsToShow(
        IEnumerable<SavegameSnapshotDto> snapshots,
        IEnumerable<SavegameCheckoutDto> claims)
    {
        var endedBySnapshot = snapshots
            .Where(x => x.CheckoutId is not null)
            .Select(x => (ClaimId: x.CheckoutId!.Value, x.Created))
            .ToHashSet();

        return [.. claims.Where(x => x.EndedAt is DateTime ended && endedBySnapshot.Contains((x.Id, ended)) is false)];
    }
}
