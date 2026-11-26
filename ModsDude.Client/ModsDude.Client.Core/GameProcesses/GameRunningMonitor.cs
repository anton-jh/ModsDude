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
    private HashSet<GameIdentity>? _connected;


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

    public IReadOnlyList<GameIdentity> Poll()
    {
        var connected = games.GetDriftCandidates()
            .Select(x => x.Identity)
            .ToHashSet();

        var running = connected
            .Where(game => processNames.Get(game) is { Count: > 0 } names && processes.IsAnyRunning(names))
            .OrderBy(x => x.ToString(), StringComparer.Ordinal)
            .ToList();

        bool changed;
        IReadOnlyList<GameIdentity> started;

        lock (_lock)
        {
            started = _connected is { } before
                ? [.. running.Where(x => before.Contains(x) && _running.Contains(x) is false)]
                : [];

            changed = running.SequenceEqual(_running) is false;
            _running = running;
            _connected = connected;
        }

        HasPolled = true;

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return started;
    }
}
