namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// How much each kind of mod list change weighs against a savegame, and the score at which checking
/// it out on the latest revision asks first.
/// </summary>
/// <remarks>
/// A game's own judgement: one with hundreds of mods shrugs off a few added, one with five mods
/// may not. A locked mod whose version changed or that was removed always asks, whatever the score.
/// </remarks>
public sealed record SavegameCompatibilityPolicy(
    int AddedWeight,
    int ChangedWeight,
    int RemovedWeight,
    int PromptThreshold);


public static class SavegameCompatibilityPolicyExtensions
{
    /// <summary>The game's policy, or null where it has no mods and so no mod list to drift from.</summary>
    public static SavegameCompatibilityPolicy? FindSavegameCompatibility(this IBaseGameAdapter adapter)
        => adapter.GetBaseCapabilityAdapterFactory<IBaseModAdapter>()?.Invoke().SavegameCompatibility;
}
