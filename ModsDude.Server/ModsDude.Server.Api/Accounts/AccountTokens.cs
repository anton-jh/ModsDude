using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Accounts;
using OpenIddict.Abstractions;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ModsDude.Server.Api.Accounts;

/// <summary>What the tokens issued to a signed-in user say, and who they are for.</summary>
public static class AccountTokens
{
    public const string ApiScope = "api";
    public const string ApiResource = "modsdude-api";

    /// <summary>OpenID Connect's prompt for opening the sign-up page instead of the sign-in page.</summary>
    public const string CreatePrompt = "create";


    /// <summary>
    /// Built from the stored account and user every time a token is issued, so a renamed user or a
    /// changed address reaches the client on its next refresh.
    /// </summary>
    public static ClaimsPrincipal CreatePrincipal(UserAccount account, User user, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(
            authenticationType: "ModsDude",
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, account.Id);
        identity.SetClaim(Claims.Email, account.Email);
        identity.SetClaim(Claims.Name, user.DisplayName.Value);

        identity.SetScopes(scopes);
        identity.SetResources(ApiResource);

        identity.SetDestinations(claim => claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Name when identity.HasScope(Scopes.Profile) => [Destinations.IdentityToken],
            Claims.Email when identity.HasScope(Scopes.Email) => [Destinations.IdentityToken],
            _ => []
        });

        return new ClaimsPrincipal(identity);
    }
}
