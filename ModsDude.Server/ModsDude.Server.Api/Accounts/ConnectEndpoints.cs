using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Accounts;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ModsDude.Server.Api.Accounts;

/// <summary>
/// The OpenID Connect endpoints OpenIddict hands to this code once it has validated the request:
/// <c>/connect/authorize</c>, which signs the browser in through the account pages, and
/// <c>/connect/token</c>, which issues tokens for a code or a refresh token.
/// </summary>
public static class ConnectEndpoints
{
    public const string AuthorizePath = "/connect/authorize";
    public const string TokenPath = "/connect/token";


    public static IEndpointRouteBuilder MapConnect(this IEndpointRouteBuilder builder)
    {
        builder.MapMethods(AuthorizePath, [HttpMethods.Get, HttpMethods.Post], Authorize)
            .AllowAnonymous()
            .ExcludeFromDescription();

        builder.MapPost(TokenPath, Token)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return builder;
    }


    private static async Task<IResult> Authorize(
        HttpContext context,
        UserManager<UserAccount> userManager,
        SignInManager<UserAccount> signInManager,
        ApplicationDbContext dbContext)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.HasPromptValue(AccountTokens.CreatePrompt))
        {
            return Results.Redirect(AccountPagePaths.SignUp(ReturnUrl(context, dropPrompt: true)));
        }

        // The prompt is dropped from the address the account pages return to, so the request coming
        // back is not sent to them again.
        if (request.HasPromptValue(PromptValues.Login))
        {
            await signInManager.SignOutAsync();

            return Results.Redirect(AccountPagePaths.SignIn(ReturnUrl(context, dropPrompt: true), request.LoginHint));
        }

        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var account = cookie.Succeeded ? await userManager.GetUserAsync(cookie.Principal) : null;
        var user = account is null ? null : await dbContext.Users.GetAsync(new UserId(account.Id), context.RequestAborted);

        if (account is null || user is null || !await signInManager.CanSignInAsync(account))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(Errors.LoginRequired, "The user is not signed in.");
            }

            await signInManager.SignOutAsync();

            return Results.Redirect(AccountPagePaths.SignIn(ReturnUrl(context, dropPrompt: false), request.LoginHint));
        }

        return Results.SignIn(
            AccountTokens.CreatePrincipal(account, user, request.GetScopes()),
            properties: null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> Token(
        HttpContext context,
        UserManager<UserAccount> userManager,
        SignInManager<UserAccount> signInManager,
        ApplicationDbContext dbContext)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Forbid(Errors.UnsupportedGrantType, "Only the authorization code and refresh token grants are supported.");
        }

        var granted = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (granted.Principal?.GetClaim(Claims.Subject) is not string subject)
        {
            return Forbid(Errors.InvalidGrant, "The grant names no user.");
        }

        var account = await userManager.FindByIdAsync(subject);
        var user = account is null ? null : await dbContext.Users.GetAsync(new UserId(account.Id), context.RequestAborted);

        if (account is null || user is null || !await signInManager.CanSignInAsync(account))
        {
            return Forbid(Errors.InvalidGrant, "The account can no longer sign in.");
        }

        return Results.SignIn(
            AccountTokens.CreatePrincipal(account, user, granted.Principal.GetScopes()),
            properties: null,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }


    private static IResult Forbid(string error, string description)
    {
        return Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    /// <summary>
    /// The authorization request as an address to come back to once the account pages are done.
    /// </summary>
    private static string ReturnUrl(HttpContext context, bool dropPrompt)
    {
        var parameters = context.Request.HasFormContentType
            ? context.Request.Form.Select(x => KeyValuePair.Create(x.Key, x.Value))
            : context.Request.Query.Select(x => KeyValuePair.Create(x.Key, x.Value));

        var kept = parameters
            .Where(x => !dropPrompt || x.Key != Parameters.Prompt)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .SelectMany(x => x.Value.Select(value => KeyValuePair.Create(x.Key, value)));

        return QueryHelpers.AddQueryString(context.Request.PathBase + context.Request.Path, kept);
    }
}
