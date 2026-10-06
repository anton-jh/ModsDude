using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.Savegames;

public sealed class SavegameOffers(
    ISavegameSlots savegameSlots,
    ISavegameBindingStore bindingStore,
    ISavegameHolds savegameHolds,
    ISyncManifestStore manifestStore,
    IProfileService profileService) : ISavegameOffers
{
    public SavegameHost? ReadHost(Repo repo)
    {
        if (repo.Games.FirstOrDefault() is not Game game)
        {
            return null;
        }

        var manifest = manifestStore.TryReadAgreed(game.TargetRefs);

        return new SavegameHost(
            game,
            bindingStore.GetBindings(game.Identity),
            savegameHolds.GetUnreachableHolds(game).Select(x => x.SavegameId).ToHashSet(),
            manifest?.ProfileId,
            manifest?.ProfileRevision);
    }

    public void Offer(Repo repo, SavegameListItemViewModel row, SavegameHost? host, Func<Guid, string?> nameOf)
    {
        if (host is null)
        {
            // Nothing connected: no buttons work, and the row says so rather than the rule doing it.
            row.SetHeldHere(null);
            row.SetOffer(null, null);

            return;
        }

        row.SetHeldHere(FindHold(row.Id, host));

        var offer = SavegameRowRules.Describe(
            row.Id,
            row.Savegame.ProfileId,
            profileService.FindLive(repo.Id, row.Savegame.ProfileId)?.HeadRevision,
            row.Hold?.PinnedRevision,
            host.Held,
            host.AppliedProfileId,
            host.AppliedRevision);

        row.SetOffer(offer, offer.ChecksInFirst is Guid blocking ? nameOf(blocking) : null);
    }

    private SavegameHoldHere? FindHold(Guid savegameId, SavegameHost host)
    {
        // Not FirstOrDefault: a binding is a struct, and its default is a fully-formed one with a
        // blank slot reference that a row would offer to disconnect.
        foreach (var binding in host.Held)
        {
            if (binding.SavegameId != savegameId)
            {
                continue;
            }

            return new SavegameHoldHere(
                host.Game,
                binding.Slot,
                savegameSlots.DescribeFolder(host.Game, binding.Slot.Target),
                host.UnreachableHolds.Contains(savegameId),
                savegameSlots.DescribeSlotNumber(host.Game, binding.Slot),
                binding.TargetRevision);
        }

        return null;
    }
}
