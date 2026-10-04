using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Invites;

/// <summary>
/// Saves an entity that carries a freshly generated <see cref="InviteCode"/> and the request ID it was
/// made for. A caller looks for a repeat of the request first, then validates, then issues.
/// </summary>
public static class CodeIssuing
{
    /// <summary>The unique index makes a repeat impossible rather than unlikely; this is for the unlikely.</summary>
    private const int _maximumCodeAttempts = 3;


    /// <param name="create">Builds the entity around a new code.</param>
    /// <param name="findByRequestId">Finds what a concurrent run of the same request saved.</param>
    public static async Task<T> IssueAsync<T>(
        this ApplicationDbContext dbContext,
        Func<InviteCode, T> create,
        Func<CancellationToken, Task<T?>> findByRequestId,
        ILogger logger,
        CancellationToken cancellationToken)
        where T : class
    {
        for (var attempt = 1; ; attempt++)
        {
            var entity = create(InviteCodes.Generate());
            dbContext.Add(entity);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return entity;
            }
            catch (DbUpdateException exception)
            {
                dbContext.Entry(entity).State = EntityState.Detached;

                if (await findByRequestId(cancellationToken) is T concurrent)
                {
                    logger.LogInformation(exception, "Issuing a {Entity} lost to a concurrent run of the same request.", typeof(T).Name);

                    return concurrent;
                }

                if (attempt >= _maximumCodeAttempts)
                {
                    throw;
                }

                logger.LogWarning(exception, "Issuing a {Entity} collided on attempt {Attempt}; trying a new code.", typeof(T).Name, attempt);
            }
        }
    }
}
