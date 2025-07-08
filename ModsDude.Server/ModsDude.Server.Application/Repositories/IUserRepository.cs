using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Application.Repositories;
public interface IUserRepository
{
    public Task<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default);
    public Task<User?> GetByUsernameAsync(Username username, CancellationToken cancellationToken = default);
    public Task<bool> CheckUsernameTakenAsync(Username username, CancellationToken cancellationToken = default);
    public void Add(User user);
}
