using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Users;

public interface ICurrentUserStore : IUserScopedState
{
    /// <summary>The signed-in user as the server last described them, or null before the first read.</summary>
    CurrentUserDto? User { get; }

    /// <summary>Raised on the store's thread whenever <see cref="User"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>The user as held, read from the server first where nothing is held yet.</summary>
    Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken);

    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>Sends a change to the signed-in user and holds the user it answers with.</summary>
    Task<CurrentUserDto> WriteAsync(Func<CancellationToken, Task<CurrentUserDto>> send, CancellationToken cancellationToken);
}
