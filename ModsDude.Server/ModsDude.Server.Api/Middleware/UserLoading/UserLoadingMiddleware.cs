using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Common;
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
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var isAuthenticated = context.User.Identity?.IsAuthenticated ?? false;
        var subClaim = context.User.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Sub);

        if (!isAuthenticated || subClaim is null)
        {
            await next(context);
            return;
        }

        var username = GetUsername(context.User);
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

        var newUser = new User(userId, username, timeService.Now());

        if (await dbContext.Users.AnyAsync(x => x.Username == username))
        {
            throw new Exception("Username of new user is not unique");
        }

        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();

        await next(context);
    }


    private static Username GetUsername(ClaimsPrincipal claimsPrincipal)
    {
        var username = claimsPrincipal.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Name)?.Value
            ?? throw new Exception("User has no 'name' claim");

        return new(username);
    }
}
