using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Changes;

/// <summary>
/// How many times what a user sees of their own record has changed: display name, picture and trust.
/// </summary>
/// <remarks>
/// Counted by the trigger the <c>UserChangeCounter</c> migration installs, never by code, so no way of
/// changing those columns can forget to count it. Columns written on every request, such as
/// <see cref="User.LastSeen"/>, are not counted.
/// </remarks>
public static class UserChanges
{
    public const string CounterColumn = "ChangeCount";


    public static Task<long> ReadForAsync(ApplicationDbContext dbContext, UserId userId, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .Where(x => x.Id == userId)
            .Select(x => EF.Property<long>(x, CounterColumn))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
