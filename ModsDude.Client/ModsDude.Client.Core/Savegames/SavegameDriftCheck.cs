using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameDriftCheck(
    IHeldSlotReader reader,
    ISyncManifestStore manifestStore,
    ISavegameSightings sightings)
    : ISavegameDriftCheck
{
    public async Task<IReadOnlyList<SavegameDrift>> CheckDriftAsync(GameIdentity game, CancellationToken ct)
    {
        var drift = new List<SavegameDrift>();

        foreach (var (binding, currentHash, slotDisplayName) in await reader.ReadAsync(game, ct))
        {
            var head = sightings.GetHeadSnapshot(binding.RepoId, binding.SavegameId);
            var claim = sightings.GetClaim(binding.RepoId, binding.SavegameId);
            var manifest = manifestStore.TryRead(new ModTargetRef(game, binding.Slot.Target));

            var kinds = SavegameDriftRules.Classify(
                binding,
                currentHash,
                head,
                manifest?.ProfileId,
                manifest?.ProfileRevision,
                claim);

            drift.AddRange(kinds.Select(kind => new SavegameDrift(binding.RepoId, binding.SavegameId, binding.Slot, kind)
            {
                SlotDisplayName = slotDisplayName,
                HeldSnapshot = binding.Snapshot,
                HeadSnapshot = head,
                PlayedRevision = binding.ProfileRevision,
                AppliedRevision = manifest?.ProfileRevision,
                TargetRevision = binding.TargetRevision,
                RunsOnAnotherProfile = binding.ProfileId is not null
                    && manifest?.ProfileId is not null
                    && binding.ProfileId != manifest.ProfileId,
                TakenBy = kind is SavegameDriftKind.TakenOver ? claim?.Holder : null
            }));
        }

        return drift;
    }
}
