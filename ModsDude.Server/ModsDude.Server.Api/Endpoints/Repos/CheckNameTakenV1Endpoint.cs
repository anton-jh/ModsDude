using ModsDude.Server.Application.Repositories;

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
        IRepoRepository repoRepository,
        CancellationToken cancellationToken)
    {
        var isTaken = await repoRepository.CheckNameIsTaken(new(request.Name), cancellationToken);

        return new(isTaken);
    }


    public record CheckNameTakenRequest(string Name);
    public record CheckNameTakenResponse(bool IsTaken);
}
