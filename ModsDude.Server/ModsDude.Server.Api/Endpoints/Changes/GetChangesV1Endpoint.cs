using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Persistence.Changes;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Api.Endpoints.Changes;

/// <summary>
/// Where the change counters of the caller and each of their repos stand. A client polls this one
/// small read and reads again only the parts whose counters moved.
/// </summary>
/// <remarks>
/// Scoped to the caller: their own record, and only the live repos they are a member of, the same set
/// as their repo list.
/// </remarks>
public class GetChangesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("changes", Get)
            .WithTags("Changes");
    }


    private static async Task<Ok<GetChangesResponse>> Get(
        HttpContext httpContext,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        var user = await UserChanges.ReadForAsync(dbContext, userId, cancellationToken);
        var repos = await RepoChanges.ReadForAsync(dbContext, userId, cancellationToken);

        return TypedResults.Ok(new GetChangesResponse(user, [.. repos.Select(x => new RepoChangesDto(
            x.RepoId.Value, x.Repo, x.Profiles, x.Savegames, x.Mods, x.Members, x.Activity))]));
    }


    /// <param name="User">The caller's own record, as <c>users/me</c> describes it.</param>
    /// <param name="Repos">Ordered by repo id.</param>
    public record GetChangesResponse(long User, IReadOnlyList<RepoChangesDto> Repos);

    /// <param name="Repo">The repo's own row: name, settings, archiving.</param>
    /// <param name="Savegames">Savegames, their snapshots and their claims.</param>
    /// <param name="Activity">Which profile each member's games are on.</param>
    public record RepoChangesDto(Guid RepoId, long Repo, long Profiles, long Savegames, long Mods, long Members, long Activity);
}
