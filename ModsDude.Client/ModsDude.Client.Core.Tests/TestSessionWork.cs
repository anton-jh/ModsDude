using ModsDude.Client.Core.Sessions;

namespace ModsDude.Client.Core.Tests;

/// <summary>A session that is only stopped when a test says so.</summary>
internal sealed class TestSessionWork : ISessionWork
{
    private readonly CancellationTokenSource _session = new();

    public CancellationToken Stopping => _session.Token;

    public Task<bool> StopAllAsync(CancellationToken cancellationToken)
    {
        _session.Cancel();

        return Task.FromResult(true);
    }
}
