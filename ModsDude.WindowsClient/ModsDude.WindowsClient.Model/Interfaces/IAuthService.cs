using ModsDude.WindowsClient.Model.Models;

namespace ModsDude.WindowsClient.Model.Interfaces;
public interface IAuthService
{
    Task<Session> GetSession(CancellationToken cancellationToken);
    Task Logout(CancellationToken cancellationToken);
}
