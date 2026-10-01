using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// What the savegames a game holds mean for its mod folder: which revision it has to be on, and which
/// applies they refuse.
/// </summary>
public interface IHeldSavegames
{
    /// <summary>
    /// Which revision this game's mod folder has to be on for one profile, or null where head is the
    /// answer: a held savegame's revision first, then the one the game itself is pinned to.
    /// </summary>
    int? GetRequiredRevision(GameIdentity game, Guid profileId);

    /// <inheritdoc cref="SavegameHoldRules.DecideApply"/>
    SavegameApplyDecision DecideApply(GameIdentity game, Guid profileId, int? revision);

    /// <inheritdoc cref="SavegameHoldRules.FindProfileHold"/>
    SavegameCheckoutBinding? FindProfileHold(GameIdentity game);
}
