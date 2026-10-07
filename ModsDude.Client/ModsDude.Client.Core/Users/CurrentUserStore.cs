using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Stores;

namespace ModsDude.Client.Core.Users;

public sealed class CurrentUserStore(
    IUsersClient usersClient,
    IStoreDispatcher dispatcher,
    ILogger<CurrentUserStore> logger)
    : ICurrentUserStore
{
    private readonly StoreLoads<WholeList> _loads = new(dispatcher, logger);
    private volatile CurrentUserDto? _user;


    public CurrentUserDto? User => _user;

    public event EventHandler? Changed;


    public async Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken)
    {
        if (_user is CurrentUserDto held)
        {
            return held;
        }

        await RefreshAsync(cancellationToken);

        return _user ?? throw new OperationCanceledException("The user changed while they were being read.");
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
        => _loads.ReadAsync(default, usersClient.GetCurrentUserV1Async, Set, cancellationToken);

    public Task<CurrentUserDto> WriteAsync(Func<CancellationToken, Task<CurrentUserDto>> send, CancellationToken cancellationToken)
        => _loads.WriteAsync(default, send, Set, cancellationToken);

    public void ClearUserState()
    {
        _loads.Reset();
        Set(null);
    }


    private void Set(CurrentUserDto? user)
    {
        _user = user;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
