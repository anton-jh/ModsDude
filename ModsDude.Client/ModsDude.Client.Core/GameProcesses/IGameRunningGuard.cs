using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.GameProcesses;

/// <summary>Refuses work on a game's files while the game is running.</summary>
public interface IGameRunningGuard
{
    /// <exception cref="GameRunningException">Any of the game's processes is alive.</exception>
    void EnsureNotRunning(GameIdentity game, string gameName);
}
