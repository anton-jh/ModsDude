using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Activity;

public interface IPresenceReporter : IUserScopedState
{
    /// <summary>
    /// Tells the server which games here are being played: a heartbeat for each running game on a
    /// profile once <see cref="PresenceReporter.HeartbeatInterval"/> has passed, and a stop for each
    /// one that has closed. Call it after every look at the process list.
    /// </summary>
    Task ReportAsync(CancellationToken cancellationToken);
}
