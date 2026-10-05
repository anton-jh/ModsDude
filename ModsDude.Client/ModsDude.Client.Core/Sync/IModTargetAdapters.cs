using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// The adapter behind one of a game's targets, for the checks that run outside an apply and so
/// have none in hand.
/// </summary>
public interface IModTargetAdapters
{
    /// <summary>
    /// The adapter and the target as its settings produce it now, or null where no loaded repo serves
    /// the game or the settings no longer produce that target.
    /// </summary>
    /// <exception cref="Exceptions.UserFriendlyException">The game's adapter refused its settings.</exception>
    ResolvedModTarget? Find(ModTargetRef target);
}

public sealed record ResolvedModTarget(ILocalModAdapter Adapter, ModTarget Target);
