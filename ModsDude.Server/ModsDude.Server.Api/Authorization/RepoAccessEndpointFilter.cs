using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Authorization;

public static class RepoAccessEndpointFilter
{
    /// <summary>
    /// Refuses the request unless the caller is a member of the route's <c>{repoId}</c> at
    /// <paramref name="minimumLevel"/> or above. Declared on the route, so the level an endpoint needs
    /// is visible where the route is.
    /// </summary>
    /// <remarks>
    /// For the plain case only. An endpoint whose repo arrives in the body, or whose rule depends on
    /// what the request asks for, checks for itself.
    /// </remarks>
    public static RouteHandlerBuilder RequireRepoLevel(this RouteHandlerBuilder builder, RepoMembershipLevel minimumLevel)
    {
        return builder
            .AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext;

                if (Guid.TryParse(http.Request.RouteValues["repoId"]?.ToString(), out var repoId) is false)
                {
                    throw new InvalidOperationException($"{http.Request.Path} requires a repo level but has no {{repoId}} in its route.");
                }

                var forbidden = await http.RequestServices.GetRequiredService<ApplicationDbContext>().Users
                    .GetAsync(http.User.GetUserId(), http.RequestAborted)
                    .CheckIsAllowedTo(x => x.AccessRepoAtLevel(new RepoId(repoId), minimumLevel))
                    .MapToForbidden();

                return forbidden is null
                    ? await next(context)
                    : forbidden;
            })
            .Produces<CustomProblemDetails>(StatusCodes.Status403Forbidden);
    }
}
