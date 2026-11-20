using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace ModsDude.Server.Api.Accounts;

/// <summary>
/// Limits how often one address may post to the account pages or ask for tokens, on top of the lockout
/// a single account gets after failed passwords.
/// </summary>
public static class AccountRateLimits
{
    private const int _accountPostsPerMinute = 10;

    /// <summary>A client refreshes once per access token, so this leaves room for many behind one address.</summary>
    private const int _tokenRequestsPerMinute = 60;


    public static RateLimitPartition<string> Partition(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter("");
        }

        if (context.Request.Path.StartsWithSegments(AccountPagePaths.Home))
        {
            return Fixed($"account:{address}", _accountPostsPerMinute);
        }

        if (context.Request.Path.StartsWithSegments(ConnectEndpoints.TokenPath))
        {
            return Fixed($"token:{address}", _tokenRequestsPerMinute);
        }

        return RateLimitPartition.GetNoLimiter("");
    }

    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";

        await context.HttpContext.Response.WriteAsync("Too many attempts. Wait a minute and try again.", cancellationToken);
    }


    private static RateLimitPartition<string> Fixed(string key, int permitsPerMinute)
    {
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    }
}
