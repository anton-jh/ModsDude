using Microsoft.AspNetCore.Authorization;

namespace ModsDude.Server.Api.Authorization;

public static class AuthorizationOptionsExtensions
{
    public static void AddApplicationPolicies(this AuthorizationOptions options)
    {
        options.AddScope(Scopes.Repo.Create);
    }


    private static void AddScope(this AuthorizationOptions options, string scope)
    {
        options.AddPolicy(scope, policy =>
        {
            policy.RequireAssertion(context => VerifyScope(context, scope));
        });
    }

    private static bool VerifyScope(AuthorizationHandlerContext context, string scope)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }
        var scopeClaim = context.User.Claims.FirstOrDefault(claim => claim.Type == "scope");
        if (scopeClaim is null)
        {
            return false;
        }
        var scopes = scopeClaim.Value.Split(' ');
        if (!scopes.Contains(scope))
        {
            return false;
        }
        return true;
    }
}
