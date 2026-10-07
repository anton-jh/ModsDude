using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Changes;

public interface IChangePoll : IUserScopedState
{
    /// <summary>
    /// Reads the server's change counters and reads again every loaded store whose counters moved.
    /// Failures are logged; whatever failed is read again on the next poll. Does nothing while the
    /// server is unreachable.
    /// </summary>
    Task PollAsync(CancellationToken cancellationToken);

    /// <summary>Reads every loaded store again, as if every counter had moved.</summary>
    Task RereadAllAsync(CancellationToken cancellationToken);
}
