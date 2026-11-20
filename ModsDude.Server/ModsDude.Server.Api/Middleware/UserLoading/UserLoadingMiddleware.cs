using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ModsDude.Server.Api.Middleware.UserLoading;

/// <summary>
/// Loads the caller named by the token, refuses one who is blocked or no longer exists, and keeps their
/// <see cref="User.LastSeen"/> current. Users are created only by signing up.
/// </summary>
public class UserLoadingMiddleware(
    ApplicationDbContext dbContext,
    ITimeService timeService)
    : IMiddleware
{
    private static readonly TimeSpan _lastSeenResolution = TimeSpan.FromHours(1);


    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var isAuthenticated = context.User.Identity?.IsAuthenticated ?? false;
        var subClaim = context.User.FindFirst(Claims.Subject);

        if (!isAuthenticated || subClaim is null)
        {
            await next(context);
            return;
        }

        var user = await dbContext.Users.FindAsync([new UserId(subClaim.Value)], context.RequestAborted);

        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(Problems.NotAuthenticated, context.RequestAborted);

            return;
        }

        if (user.IsBlocked)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(Problems.UserBlocked, context.RequestAborted);

            return;
        }

        await TouchUserAsync(user, context.RequestAborted);

        await next(context);
    }


    /// <summary>Only once per <see cref="_lastSeenResolution"/>.</summary>
    private async Task TouchUserAsync(User user, CancellationToken cancellationToken)
    {
        var now = timeService.Now();

        if (now - user.LastSeen <= _lastSeenResolution)
        {
            return;
        }

        user.LastSeen = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
