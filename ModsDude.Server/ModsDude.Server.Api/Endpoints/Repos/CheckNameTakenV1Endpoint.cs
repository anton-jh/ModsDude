using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Repos;

public class CheckNameTakenV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/check-name-taken", CheckNameTaken)
            .RequireAuthorization()
            .WithTags("Repos");
    }


    private static async Task<CheckNameTakenResponse> CheckNameTaken(
        CheckNameTakenRequest request,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var isTaken = await dbContext.Repos.CheckNameIsTaken(new(request.Name), cancellationToken);

        return new(isTaken);
    }


    public record CheckNameTakenRequest(string Name);
    public record CheckNameTakenResponse(bool IsTaken);
}
