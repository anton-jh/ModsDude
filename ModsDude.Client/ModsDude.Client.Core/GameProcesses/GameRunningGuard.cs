using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.GameProcesses;

public sealed class GameRunningGuard(IGameProcessNames processNames, IGameProcesses processes) : IGameRunningGuard
{
    public void EnsureNotRunning(GameIdentity game, string gameName)
    {
        var names = processNames.Get(game) ?? throw GameRunningException.Unknown(gameName);

        if (processes.IsAnyRunning(names))
        {
            throw new GameRunningException(gameName);
        }
    }
}
