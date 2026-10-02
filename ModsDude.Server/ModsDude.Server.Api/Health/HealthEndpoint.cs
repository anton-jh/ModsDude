using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Builds;

namespace ModsDude.Server.Api.Health;

/// <summary>
/// <c>GET /health</c>: which build is up and answering requests. Anonymous and outside the API and its
/// document, because a deploy checks it before anyone has a token.
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


    private static Ok<HealthResponse> Get() => TypedResults.Ok(new HealthResponse(ServerBuild.Number));
}

public sealed record HealthResponse(int Build);
