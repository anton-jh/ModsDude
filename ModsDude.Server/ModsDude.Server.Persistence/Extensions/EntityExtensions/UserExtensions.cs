using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class UserExtensions
{
    public static object[] GetKey(this User user)
    {
        return [user.Id];
    }

    public static object[] GetKey(UserId userId)
    {
        return [userId];
    }

    public static Task<bool> CheckUsernameTakenAsync(this DbSet<User> dbSet, Username username, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(user => user.Username == username, cancellationToken);
    }

    public static async Task<User?> GetAsync(this DbSet<User> dbSet, UserId userId, CancellationToken cancellationToken)
    {
        return await dbSet.FindAsync(GetKey(userId), cancellationToken);
    }

    public static Task<User?> GetByUsernameAsync(this DbSet<User> dbSet, Username username, CancellationToken cancellationToken)
    {
        return dbSet.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }
}
