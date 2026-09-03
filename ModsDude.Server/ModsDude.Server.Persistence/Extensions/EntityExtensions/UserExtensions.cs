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

    /// <summary>
    /// What the named users are called, for a list that has ids and needs names - a profile's
    /// history, where a page of revisions has a handful of distinct authors between them.
    /// </summary>
    /// <remarks>
    /// A second query rather than a join, because joining would have to produce a nullable value
    /// object inside a projection and a provider declines to translate that. An id with no row is
    /// simply absent: the caller falls back to the id rather than dropping the row it belongs to.
    /// </remarks>
    public static async Task<Dictionary<UserId, DisplayName>> GetDisplayNamesAsync(
        this DbSet<User> dbSet,
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var rows = await dbSet
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id, x => x.DisplayName);
    }
}
