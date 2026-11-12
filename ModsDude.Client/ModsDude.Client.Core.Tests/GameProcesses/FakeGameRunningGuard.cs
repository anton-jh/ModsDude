using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;

namespace ModsDude.Client.Core.Tests.GameProcesses;

internal sealed class FakeGameRunningGuard : IGameRunningGuard
{
    public bool Running { get; set; }

    /// <summary>The game starts as this many checks have been made, for a game launched part way through.</summary>
    public int? StartsAfterChecks { get; set; }

    public int Checks { get; private set; }

    public void EnsureNotRunning(GameIdentity game, string gameName)
    {
        if (Checks++ >= StartsAfterChecks)
        {
            Running = true;
        }

        if (Running)
        {
            throw new GameRunningException(gameName);
        }
    }
}
