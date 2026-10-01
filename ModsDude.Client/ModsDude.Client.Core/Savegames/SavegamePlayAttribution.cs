using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegamePlayAttribution(
    ISavegameBindingStore bindings,
    IHeldSlotReader reader,
    ISyncManifestStore manifestStore,
    ILogger<SavegamePlayAttribution> logger)
    : ISavegamePlayAttribution
{
    public async Task ObserveAsync(ModTargetRef target, CancellationToken ct)
    {
        // A savegame with no profile claims no mod list, so there is nothing to attribute its play to.
        var held = bindings.GetBindingsIn(target).Where(x => x.ProfileId is not null).ToList();

        if (held.Count == 0)
        {
            return;
        }

        // Read before anything is hashed: it is the outgoing revision only until the apply rewrites it.
        var manifest = manifestStore.TryRead(target);

        foreach (var reading in await reader.ReadAsync(target.Game, held, ct))
        {
            if (reading.CurrentHash is string current)
            {
                Observe(target.Game, reading.Binding, current, manifest?.ProfileId, manifest?.ProfileRevision);
            }
        }
    }

    public SavegameCheckoutBinding Observe(GameIdentity game, SavegameCheckoutBinding binding, string currentContentHash)
    {
        var manifest = ReadAppliedManifest(game, binding);

        return Observe(game, binding, currentContentHash, manifest?.ProfileId, manifest?.ProfileRevision);
    }

    public int? FindPlayedRevision(GameIdentity game, SavegameCheckoutBinding binding)
    {
        if (binding.ProfileId is not Guid profileId)
        {
            return null;
        }

        if (binding.LastPlayedRevision is int played)
        {
            return played;
        }

        var manifest = ReadAppliedManifest(game, binding);

        // Revisions of two different profiles are not comparable, and the server refuses a revision
        // that is not the savegame's profile's.
        if (manifest?.ProfileRevision is int applied && profileId == manifest.ProfileId)
        {
            return applied;
        }

        return binding.ProfileRevision;
    }

    public int? GetPlayedRevision(Game game, Guid savegameId)
        => bindings.GetBinding(game.Identity, savegameId) is SavegameCheckoutBinding binding
            ? FindPlayedRevision(game.Identity, binding)
            : null;

    /// <remarks>
    /// A folder on another profile, or on none, leaves <see cref="SavegameCheckoutBinding.LastPlayedRevision"/>
    /// where it was: there is nothing to credit this play to, and the last known revision stays truer
    /// than none. The hash still moves, because the bytes did.
    /// </remarks>
    private SavegameCheckoutBinding Observe(
        GameIdentity game,
        SavegameCheckoutBinding binding,
        string currentContentHash,
        Guid? appliedProfileId,
        int? appliedRevision)
    {
        if (binding.ProfileId is not Guid profileId || ModContentHasher.Matches(currentContentHash, binding.LastObservedHash))
        {
            return binding;
        }

        var played = profileId == appliedProfileId ? appliedRevision : null;

        var observed = binding with
        {
            LastObservedHash = currentContentHash,
            LastPlayedRevision = played ?? binding.LastPlayedRevision
        };

        bindings.SetBinding(game, observed);

        logger.LogInformation(
            "Savegame {Savegame} in game {Game} has been played since it was last looked at; attributed to profile revision {Revision}.",
            binding.SavegameId, game, observed.LastPlayedRevision);

        return observed;
    }

    /// <summary>The manifest of the mod folder paired with the savegame folder the binding is in.</summary>
    private SyncManifest? ReadAppliedManifest(GameIdentity game, SavegameCheckoutBinding binding)
        => manifestStore.TryRead(new ModTargetRef(game, binding.Slot.Target));
}
