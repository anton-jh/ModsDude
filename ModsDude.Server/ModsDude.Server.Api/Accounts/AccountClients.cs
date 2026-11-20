using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ModsDude.Server.Api.Accounts;

/// <summary>
/// The applications allowed to ask for tokens. Brought in line with this list at every startup, so a
/// client changed or dropped here is changed or dropped in the database too.
/// </summary>
public static class AccountClients
{
    public const string DesktopClientId = "modsdude-client";
    public const string SwaggerClientId = "swagger";

    /// <summary>
    /// The desktop client listens on a loopback port it picks itself. Registered as a native application,
    /// so any port is accepted on this address.
    /// </summary>
    public const string DesktopRedirectUri = "http://localhost/callback/";


    public static async Task EnsureRegisteredAsync(IServiceProvider services, IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        var wanted = Describe(environment).ToDictionary(x => x.ClientId!, StringComparer.Ordinal);

        // Read in full before deleting anything: the listing is a query still streaming from the database.
        var registered = new List<object>();
        await foreach (var application in manager.ListAsync(count: null, offset: null, cancellationToken))
        {
            registered.Add(application);
        }

        foreach (var application in registered)
        {
            if (await manager.GetClientIdAsync(application, cancellationToken) is not string clientId
                || !wanted.ContainsKey(clientId))
            {
                await manager.DeleteAsync(application, cancellationToken);
            }
        }

        foreach (var descriptor in wanted.Values.OrderBy(x => x.ClientId, StringComparer.Ordinal))
        {
            if (await manager.FindByClientIdAsync(descriptor.ClientId!, cancellationToken) is { } existing)
            {
                await manager.UpdateAsync(existing, descriptor, cancellationToken);
            }
            else
            {
                await manager.CreateAsync(descriptor, cancellationToken);
            }
        }
    }


    private static IEnumerable<OpenIddictApplicationDescriptor> Describe(IWebHostEnvironment environment)
    {
        yield return Public(DesktopClientId, "ModsDude", ApplicationTypes.Native, DesktopRedirectUri);

        // Only where the Swagger UI is served.
        if (environment.IsDevelopment())
        {
            yield return Public(SwaggerClientId, "Swagger UI", ApplicationTypes.Web, "https://localhost:7035/swagger/oauth2-redirect.html");
        }
    }

    private static OpenIddictApplicationDescriptor Public(string clientId, string displayName, string applicationType, string redirectUri)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = displayName,
            ClientType = ClientTypes.Public,
            ApplicationType = applicationType,
            ConsentType = ConsentTypes.Implicit,
            RedirectUris = { new Uri(redirectUri) },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + AccountTokens.ApiScope
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange
            }
        };

        return descriptor;
    }
}
