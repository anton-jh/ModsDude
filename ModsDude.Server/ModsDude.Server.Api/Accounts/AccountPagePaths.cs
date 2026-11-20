using Microsoft.AspNetCore.WebUtilities;

namespace ModsDude.Server.Api.Accounts;

/// <summary>The addresses of the account pages, for redirects from outside them.</summary>
public static class AccountPagePaths
{
    public const string Home = "/account";


    public static string SignIn(string? returnUrl, string? email = null)
        => With("/account/sign-in", ("returnUrl", returnUrl), ("email", email));

    public static string SignUp(string? returnUrl)
        => With("/account/sign-up", ("returnUrl", returnUrl));


    private static string With(string path, params (string Name, string? Value)[] parameters)
    {
        var present = parameters
            .Where(x => !string.IsNullOrEmpty(x.Value))
            .Select(x => KeyValuePair.Create(x.Name, x.Value));

        return QueryHelpers.AddQueryString(path, present);
    }
}
