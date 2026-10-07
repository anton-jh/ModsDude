using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Changes;

public interface IChangePoll : IUserScopedState
{
    /// <summary>
    /// Reads the server's change counters and reads again every loaded store whose counters moved.
    /// Failures are logged; whatever failed is read again on the next poll.
    /// </summary>
    Task PollAsync(CancellationToken cancellationToken);
}
