using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Exceptions;

namespace ModsDude.Server.Api.Middleware.ErrorHandling;

/// <summary>
/// Turns <see cref="NotAuthenticatedException"/> into a 401 carrying the typed problem body.
/// </summary>
/// <remarks>
/// It is caught centrally rather than expressed in each endpoint's <c>Results&lt;...&gt;</c> union
/// for two reasons. It is thrown from inside <c>CheckIsAllowedTo</c> and <c>GetUserId</c> — below the
/// handler, where there is no result to return — and the endpoints most able to raise it
/// (<c>GET users</c>, <c>GET repos</c>) return a bare <c>Ok&lt;T&gt;</c> with no union to put it in.
/// Until this existed it was an unhandled exception and every one of those cases answered 500.
/// </remarks>
public class NotAuthenticatedMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (NotAuthenticatedException) when (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            await context.Response.WriteAsJsonAsync(Problems.NotAuthenticated, context.RequestAborted);
        }
    }
}
