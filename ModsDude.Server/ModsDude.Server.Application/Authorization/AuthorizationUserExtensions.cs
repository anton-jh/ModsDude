using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Application.Authorization;

public static class AuthorizationUserExtensions
{
    public static AuthorizationResult? CheckIsAllowedTo(this User? user, Action<FluentAuthorizationBuilder> authorizationAction)
    {
        if (user is null)
        {
            throw new NotAuthenticatedException();
        }

        var builder = new FluentAuthorizationBuilder(user);
        authorizationAction(builder);
        return builder.Result;
    }

    public static async Task<AuthorizationResult?> CheckIsAllowedTo(this Task<User?> userTask, Action<FluentAuthorizationBuilder> authorizationAction)
    {
        var user = await userTask;
        return CheckIsAllowedTo(user, authorizationAction);
    }
}
