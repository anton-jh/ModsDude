using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.GameProcesses;

/// <summary>
/// Which processes are a game running. Answered through whichever loaded repo serves the game, since
/// the adapter hydrates from a repo's base settings, which a game does not carry.
/// </summary>
public interface IGameProcessNames
{
    /// <returns>Empty where no loaded repo serves the game, or its adapter cannot name a process.</returns>
    IReadOnlyList<string> Get(GameIdentity game);
}
