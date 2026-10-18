using ModsDude.Server.Application.Services;

namespace ModsDude.Server.Api.Deploy;

public interface IUpdateAnnouncement
{
    /// <summary>When the update on its way was announced, in UTC. Null where none is.</summary>
    DateTime? AnnouncedAt { get; }

    /// <summary>Idempotent: an announcement already standing keeps its time.</summary>
    void Announce();

    void Withdraw();
}


/// <summary>
/// That CI has started shipping a build which will replace this server, so that clients can warn
/// their users before the API goes down.
/// </summary>
/// <remarks>
/// In memory: the deploy restarts the API, so the build it brings up starts with no announcement. An
/// announcement nothing withdraws, such as one from a run whose runner was lost, lapses on its own.
/// </remarks>
public sealed class UpdateAnnouncement(ITimeService timeService) : IUpdateAnnouncement
{
    /// <summary>Well past how long a run takes from starting to replacing the API.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    private readonly Lock _lock = new();
    private DateTime? _announcedAt;


    public DateTime? AnnouncedAt
    {
        get
        {
            lock (_lock)
            {
                return Standing();
            }
        }
    }

    public void Announce()
    {
        lock (_lock)
        {
            _announcedAt = Standing() ?? timeService.Now();
        }
    }

    public void Withdraw()
    {
        lock (_lock)
        {
            _announcedAt = null;
        }
    }


    private DateTime? Standing()
        => _announcedAt is DateTime at && timeService.Now() - at < Lifetime ? at : null;
}
