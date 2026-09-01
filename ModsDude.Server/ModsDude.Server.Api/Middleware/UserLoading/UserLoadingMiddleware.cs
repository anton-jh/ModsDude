using Microsoft.EntityFrameworkCore;
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
        var displayName = GetDisplayName(context.User);
        var existingUser = await dbContext.Users.FindAsync(userId);

        if (existingUser is not null)
        {
            await RefreshUserAsync(existingUser, displayName);
        }
        else
        {
            await ProvisionUserAsync(userId, displayName, context.RequestAborted);
        }

        await next(context);
    }


    /// <summary>
    /// The name belongs to the identity provider, so it is re-read on every request rather than
    /// frozen at provisioning: somebody who renames themselves there is renamed here, and their
    /// teammates see it. Nothing has to be resolved for them first - the name is not unique, so
    /// there is no other user it can be in the way of.
    /// </summary>
    /// <remarks>
    /// A rename writes immediately; an unchanged name rides the <see cref="User.LastSeen"/> throttle,
    /// because that write is the only reason to touch the row at all.
    /// </remarks>
    private async Task RefreshUserAsync(User user, DisplayName displayName)
    {
        var now = timeService.Now();
        var isRenamed = user.DisplayName != displayName;

        if (!isRenamed && now - user.LastSeen <= _lastSeenResolution)
        {
            return;
        }

        user.DisplayName = displayName;
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
