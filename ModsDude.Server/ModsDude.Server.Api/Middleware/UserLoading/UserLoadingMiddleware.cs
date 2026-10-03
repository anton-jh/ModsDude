using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ModsDude.Server.Api.Middleware.UserLoading;

public class UserLoadingMiddleware(
    ApplicationDbContext dbContext,
    ITimeService timeService)
    : IMiddleware
{
    private static readonly TimeSpan _lastSeenResolution = TimeSpan.FromHours(1);


    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var isAuthenticated = context.User.Identity?.IsAuthenticated ?? false;
        var subClaim = context.User.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Sub);

        if (!isAuthenticated || subClaim is null)
        {
            await next(context);
            return;
        }

        var userId = new UserId(subClaim.Value);
        var existingUser = await dbContext.Users.FindAsync(userId);

        if (existingUser is { IsBlocked: true })
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(Problems.UserBlocked, context.RequestAborted);

            return;
        }

        if (existingUser is not null)
        {
            await TouchUserAsync(existingUser);
        }
        else
        {
            await ProvisionUserAsync(userId, GetDisplayName(context.User), context.RequestAborted);
        }

        await next(context);
    }


    /// <summary>
    /// Only <see cref="User.LastSeen"/>, and only once per <see cref="_lastSeenResolution"/>. The
    /// name claim is <i>not</i> read again: it seeded the name at provisioning and the name is the
    /// user's own from then on - see <see cref="DisplayName"/>.
    /// </summary>
    private async Task TouchUserAsync(User user)
    {
        var now = timeService.Now();

        if (now - user.LastSeen <= _lastSeenResolution)
        {
            return;
        }

        user.LastSeen = now;

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// The subject id is the identity and the primary key both, so this insert can only ever land on
    /// this subject's own row. What it races is another request for the same brand-new user, and the
    /// loser of that race has nothing left to do.
    /// </summary>
    private async Task ProvisionUserAsync(UserId userId, DisplayName displayName, CancellationToken cancellationToken)
    {
        var newUser = new User(userId, displayName, timeService.Now());
        dbContext.Users.Add(newUser);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // AsNoTracking so the answer comes from the database rather than from the Added entity
            // this very method is holding, which the identity map would otherwise return.
            if (!await dbContext.Users.AsNoTracking().AnyAsync(x => x.Id == userId, cancellationToken))
            {
                throw;
            }

            dbContext.Entry(newUser).State = EntityState.Detached;
        }
    }

    private static DisplayName GetDisplayName(ClaimsPrincipal claimsPrincipal)
    {
        return DisplayName.FromClaim(
            claimsPrincipal.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Name)?.Value);
    }
}
