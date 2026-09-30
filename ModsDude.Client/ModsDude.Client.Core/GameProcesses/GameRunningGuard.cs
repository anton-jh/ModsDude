using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.GameProcesses;

public sealed class GameRunningGuard(IGameProcessNames processNames, IGameProcesses processes) : IGameRunningGuard
{
    public void EnsureNotRunning(GameIdentity game, string gameName)
    {
        if (processes.IsAnyRunning(processNames.Get(game)))
        {
            throw new GameRunningException(gameName);
        }
    }
}
