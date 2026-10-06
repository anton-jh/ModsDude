using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

public sealed class HeldSavegames(ISavegameBindingStore bindings) : IHeldSavegames
{
    public int? GetRequiredRevision(GameIdentity game, Guid profileId)
        => SavegameHoldRules.RequiredRevision(bindings.GetBindings(game), profileId)
            ?? bindings.GetPinnedRevision(game, profileId);

    public SavegameApplyDecision DecideApply(GameIdentity game, Guid profileId, int? revision)
        => SavegameHoldRules.DecideApply(bindings.GetBindings(game), profileId, revision);

    public SavegameCheckoutBinding? FindProfileHold(GameIdentity game)
        => SavegameHoldRules.FindProfileHold(bindings.GetBindings(game));

    public SavegameKeepPlan DecideKeepPublished(Game game, Guid repoId, Guid? profileId)
        => SavegameHoldRules.DecideKeepPublished(bindings.GetBindings(game.Identity), game.ActiveProfile, repoId, profileId);
}
