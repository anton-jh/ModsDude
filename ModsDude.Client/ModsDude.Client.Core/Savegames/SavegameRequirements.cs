using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>The refusals every savegame verb shares, each with the sentence the user sees.</summary>
internal static class SavegameRequirements
{
    public static ILocalSavegameAdapter Require(this ILocalSavegameAdapters adapters, Game game)
        => adapters.TryGet(game)
            ?? throw new UserFriendlyException(
                $"'{game.Name}' has no savegames",
                $"No loaded repo hydrates a savegame adapter for game '{game.Identity}' - either its game does not support savegames, or no repo on this machine serves its scope.");

    /// <summary>
    /// The savegame folder a slot reference addresses. A target the settings no longer name is refused
    /// rather than substituted with another of the game's folders.
    /// </summary>
    public static SavegameTarget RequireTarget(this ILocalSavegameAdapter adapter, Game game, SavegameSlotRef slot)
        => adapter.SavegameTargets[slot.Target]
            ?? throw new UserFriendlyException(
                $"'{game.Name}' no longer has the folder that save is in",
                $"No savegame folder is configured for target '{slot.Target}' of game '{game.Identity}', so slot '{slot.Slot}' cannot be reached. Point the settings back at it, or disconnect the savegame to stop tracking it here.");

    /// <summary>
    /// Refuses to take a second savegame that claims this game's mod folder. A savegame with no profile
    /// claims nothing, so it is never refused here.
    /// </summary>
    public static void EnsureModFolderIsFree(
        this ISavegameBindingStore bindings,
        Game game,
        Guid savegameId,
        Guid? profileId,
        string savegameName)
    {
        if (profileId is null)
        {
            return;
        }

        if (SavegameHoldRules.FindConflictingHold(bindings.GetBindings(game.Identity), savegameId) is not SavegameCheckoutBinding blocking)
        {
            return;
        }

        throw new UserFriendlyException(
            $"'{game.Name}' is already holding a savegame",
            $"Savegame '{blocking.SavegameId}' is checked out in game '{game.Identity}' and follows profile '{blocking.ProfileId}', so its mod folder is spoken for. Check that one in before taking '{savegameName}'.");
    }

    public static SavegameCheckoutBinding RequireBinding(this ISavegameBindingStore bindings, Game game, Guid savegameId)
        => bindings.GetBinding(game.Identity, savegameId)
            ?? throw new UserFriendlyException(
                "This machine is not holding that savegame",
                $"No checkout binding for savegame '{savegameId}' in game '{game.Identity}'. Only the machine that checked a save out can check it in or discard it.");
}
