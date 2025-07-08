using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Repositories;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Repositories;
public class UserRepository(ApplicationDbContext dbContext) : IUserRepository
{
    public void Add(User user)
    {
        dbContext.Users.Add(user);
    }

    public async Task<bool> CheckUsernameTakenAsync(Username username, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AnyAsync(user => user.Username == username, cancellationToken);
    }

    public async Task<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .FindAsync(new object[] { userId }, cancellationToken);
    }

    public async Task<User?> GetByUsernameAsync(Username username, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }
}
