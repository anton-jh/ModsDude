using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Users;

namespace ModsDude.Client.Core.Tests.Users;

public sealed class FixedCurrentUser(string id) : ICurrentUserStore
{
    public CurrentUserDto? User { get; } = new() { Id = id, DisplayName = id };

    public event EventHandler? Changed { add { } remove { } }

    public Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken) => Task.FromResult(User!);

    public int Refreshes { get; private set; }

    public bool FailNext { get; set; }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        Refreshes++;

        if (FailNext)
        {
            FailNext = false;

            throw new HttpRequestException("The connection dropped.");
        }

        return Task.CompletedTask;
    }

    public Task<CurrentUserDto> WriteAsync(Func<CancellationToken, Task<CurrentUserDto>> send, CancellationToken cancellationToken)
        => send(cancellationToken);

    public void ClearUserState()
    {
    }
}
