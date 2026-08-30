using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;

namespace ModsDude.Server.Api.Authorization;

public static class AuthorizationResultExtensions
{
    /// <summary>
    /// 403 rather than 401, always: every endpoint group requires authentication, so a request that
    /// reaches a handler has already established who it is and an <see cref="AuthorizationResult"/>
    /// can only mean it may not do this. The unidentifiable caller — no <c>sub</c>, or a subject with
    /// no user row — surfaces as <see cref="Application.Exceptions.NotAuthenticatedException"/> and is
    /// turned into a 401 centrally, because the endpoints that need it most return no
    /// <c>Results&lt;...&gt;</c> union to put it in.
    /// </summary>
    public static Forbidden<CustomProblemDetails>? MapToForbidden(this AuthorizationResult? result)
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

        return new Forbidden<CustomProblemDetails>(problem);
    }

    public static async Task<Forbidden<CustomProblemDetails>?> MapToForbidden(this Task<AuthorizationResult?> resultTask)
    {
        return MapToForbidden(await resultTask);
    }
}
