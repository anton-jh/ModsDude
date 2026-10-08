using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.GameProcesses;

/// <summary>Which of this machine's connected games were running at the last look.</summary>
public interface IGameRunningMonitor
{
    /// <summary>Raised by <see cref="Poll"/>, on the thread that called it, when a game started or stopped.</summary>
    event EventHandler? Changed;

    /// <summary>Whether <see cref="Poll"/> has looked at all yet.</summary>
    bool HasPolled { get; }

    bool IsRunning(GameIdentity game);

    /// <summary>The running games, in identity order.</summary>
    IReadOnlyList<GameIdentity> Running { get; }

    /// <summary>Looks at the process list for every connected game.</summary>
    void Poll();
}
