using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;

namespace ModsDude.Server.Api.Authorization;

public static class AuthorizationResultExtensions
{
    public static BadRequest<CustomProblemDetails>? MapToBadRequest(this AuthorizationResult? result)
    {
        if (result is null)
        {
            return null;
        }

        var problem = result switch
        {
            AuthorizationResult.InsufficientRepoAccess res => Problems.InsufficientRepoAccess(res.Needed),
            _ => Problems.NotAuthorized
        };

        return TypedResults.BadRequest(problem);
    }

    public static async Task<BadRequest<CustomProblemDetails>?> MapToBadRequest(this Task<AuthorizationResult?> resultTask)
    {
        return MapToBadRequest(await resultTask);
    }
}
