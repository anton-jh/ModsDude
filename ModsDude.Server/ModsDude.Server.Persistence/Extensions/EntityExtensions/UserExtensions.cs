using ModsDude.Server.Domain.Users;
using Microsoft.EntityFrameworkCore;

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

    public static async Task<User?> GetAsync(this DbSet<User> dbSet, UserId userId, CancellationToken cancellationToken)
    {
        return await dbSet.FindAsync(GetKey(userId), cancellationToken);
    }
}
