using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Activity;

/// <summary>
/// Which profile each of the caller's friends has their games on, most recently active first.
/// </summary>
/// <remarks>
/// <para>
/// <b>A friend is somebody the caller shares a repo with, and only in that repo.</b> A row is shown
/// where both of them are members of the repo it names - so somebody who switched onto a profile in
/// a repo the caller is not in simply drops off the caller's list, rather than going on showing
/// the profile they have left. See <see cref="GameActivityExtensions.GetVisibleToAsync"/>.
/// </para>
/// <para>
/// <b>The last week only.</b> What somebody set their game to a month ago says nothing about what
/// they are playing now, and a list of everybody who ever activated anything would bury the people
/// who are.
/// </para>
/// <para>
/// The caller's own rows are left out: their own machine says what they are on, and another of
/// their machines is not a friend.
/// </para>
/// </remarks>
public class GetGameActivityV1Endpoint : IEndpoint
{
    /// <summary>How far back a row is still worth showing.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("activity", Get)
            .WithTags("Activity");
    }


    /// <param name="repoId">Only this repo's rows, for its overview. Every repo the caller is in where null.</param>
    private static async Task<Ok<IEnumerable<GameActivityDto>>> Get(
        Guid? repoId,
        HttpContext httpContext,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.GetVisibleToAsync(
            httpContext.User.GetUserId(),
            repoId is Guid only ? new RepoId(only) : null,
            timeService.Now() - Window,
            cancellationToken);

        return TypedResults.Ok(rows.Select(x => new GameActivityDto(
            UserDto.FromModel(x.User),
            x.Activity.Game.Value,
            x.Activity.RepoId.Value,
            x.Activity.ProfileId.Value,
            x.ProfileName.Value,
            x.Activity.PinnedRevision?.Value,
            x.Activity.Kind,
            x.Activity.SavegameId?.Value,
            x.SavegameName?.Value,
            x.Activity.ChangedAt,
            x.Activity.TouchedAt)));
    }
}
