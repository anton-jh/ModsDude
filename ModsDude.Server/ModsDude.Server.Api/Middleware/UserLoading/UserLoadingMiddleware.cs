using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ModsDude.Server.Api.Middleware.UserLoading;

public class UserLoadingMiddleware(
    ApplicationDbContext dbContext,
    ITimeService timeService)
    : IMiddleware
{
    /// <summary>
    /// Two requests from the same brand-new user, or from two users sharing a display name, can
    /// resolve a name and then both try to write it. The unique index is what settles that, and a
    /// retry is what turns losing the race into a resolved name rather than a failed first request.
    /// </summary>
    private const int _maximumProvisioningAttempts = 3;


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

        if (existingUser is not null)
        {
            if (timeService.Now() - existingUser.LastSeen > TimeSpan.FromHours(1))
            {
                existingUser.LastSeen = timeService.Now();
                await dbContext.SaveChangesAsync();
            }

            await next(context);
            return;
        }

        await ProvisionUserAsync(userId, GetDesiredUsername(context.User), context.RequestAborted);

        await next(context);
    }


    /// <summary>
    /// The subject id is the identity; the username is only a label, and is chosen to be free rather
    /// than asserted to be. Nothing here can reach a row belonging to another subject: the insert
    /// carries this subject as its key, and a name that turns out to be taken loses at the index and
    /// is re-resolved rather than overwritten.
    /// </summary>
    private async Task ProvisionUserAsync(UserId userId, Username desiredUsername, CancellationToken cancellationToken)
    {
        var newUser = new User(userId, desiredUsername, timeService.Now());
        dbContext.Users.Add(newUser);

        for (var attempt = 1; ; attempt++)
        {
            newUser.Username = await FindFreeUsernameAsync(desiredUsername, cancellationToken);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt < _maximumProvisioningAttempts)
            {
                // AsNoTracking so the answer comes from the database rather than from the Added
                // entity this very method is holding, which the identity map would otherwise return.
                if (await dbContext.Users.AsNoTracking().AnyAsync(x => x.Id == userId, cancellationToken))
                {
                    // Another request provisioned this same user while this one was deciding on a
                    // name. Nothing left to do, and this request must not try to insert it again.
                    dbContext.Entry(newUser).State = EntityState.Detached;
                    return;
                }
            }
        }
    }

    private async Task<Username> FindFreeUsernameAsync(Username desired, CancellationToken cancellationToken)
    {
        foreach (var candidate in UsernameAllocator.GetCandidates(desired))
        {
            if (!await dbContext.Users.CheckUsernameTakenAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new UsernameTakenException();
    }

    private static Username GetDesiredUsername(ClaimsPrincipal claimsPrincipal)
    {
        return UsernameAllocator.FromDisplayName(
            claimsPrincipal.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Name)?.Value);
    }
}
