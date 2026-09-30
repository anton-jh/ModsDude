using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;

namespace ModsDude.Client.Core.Tests.GameProcesses;

internal sealed class FakeGameRunningGuard : IGameRunningGuard
{
    public bool Running { get; set; }

    public void EnsureNotRunning(GameIdentity game, string gameName)
    {
        if (Running)
        {
            throw new GameRunningException(gameName);
        }
    }
}
