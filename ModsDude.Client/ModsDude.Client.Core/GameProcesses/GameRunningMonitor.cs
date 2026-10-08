using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.GameProcesses;

/// <summary>
/// The one place that polls the process list for games, so the play-session reminder, presence and
/// the pages all agree on what is running.
/// </summary>
/// <remarks>
/// Not what guards file work: <see cref="IGameRunningGuard"/> asks the process list itself at the
/// moment it matters, because a look from a few seconds ago is not good enough to write by.
/// </remarks>
public sealed class GameRunningMonitor(
    IDriftCandidateSource games,
    IGameProcessNames processNames,
    IGameProcesses processes)
    : IGameRunningMonitor
{
    private readonly Lock _lock = new();
    private IReadOnlyList<GameIdentity> _running = [];


    public event EventHandler? Changed;


    public bool HasPolled { get; private set; }

    public IReadOnlyList<GameIdentity> Running
    {
        get
        {
            lock (_lock)
            {
                return _running;
            }
        }
    }


    public bool IsRunning(GameIdentity game) => Running.Contains(game);

    public void Poll()
    {
        var running = games.GetDriftCandidates()
            .Select(x => x.Identity)
            .Where(game => processNames.Get(game) is { Count: > 0 } names && processes.IsAnyRunning(names))
            .OrderBy(x => x.ToString(), StringComparer.Ordinal)
            .ToList();

        bool changed;

        lock (_lock)
        {
            changed = running.SequenceEqual(_running) is false;
            _running = running;
        }

        HasPolled = true;

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
