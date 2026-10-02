using Microsoft.AspNetCore.Http.HttpResults;

namespace ModsDude.Server.Api.Health;

/// <summary>
/// <c>GET /health</c>: whether the API process is up and answering requests at all. Anonymous and outside
/// the API and its document, because a deploy checks it before anyone has a token.
/// </summary>
public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("health", Get)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return builder;
    }


    private static Ok Get() => TypedResults.Ok();
}
