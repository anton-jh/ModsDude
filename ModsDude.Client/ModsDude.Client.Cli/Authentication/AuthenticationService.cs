using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Cli.Authentication;

internal class AuthenticationService : IAccessTokenAccessor
{
    private const string _clientId = "17e5db7c-9023-40cd-9cd8-3c49b7f98927";
    private static readonly string _authority = "https://modsdudeexternal.ciamlogin.com/cce54c8f-87a3-4c39-a558-9a15733d2cdf/susi_1/v2.0";
    private static readonly string _redirectUri = "http://localhost";
    private static readonly string[] _scopes = ["api://modsdude-server/act_as_user", "openid", "offline_access"];
    private readonly IPublicClientApplication _client;
    private bool _tokenCacheConfigured = false;


    public AuthenticationService()
    {
        _client = PublicClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority(_authority)
            .WithRedirectUri(_redirectUri)
            .Build();
    }


    public async Task<string> Get(CancellationToken cancellationToken)
    {
        if (!_tokenCacheConfigured)
        {
            await ConfigureTokenCacheAsync();
        }

        AuthenticationResult? result = null;

        var accounts = (await _client.GetAccountsAsync()).ToList();

        if (accounts.Count != 0)
        {
            try
            {
                result = await _client.AcquireTokenSilent(_scopes, accounts.First()).ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException)
            {
                result = await _client.AcquireTokenInteractive(_scopes).ExecuteAsync(cancellationToken);
            }
        }

        if (result is null)
        {
            result = await _client.AcquireTokenInteractive(_scopes).ExecuteAsync(cancellationToken);
        }

        return result.AccessToken;
    }


    public async Task ForceRelogin(CancellationToken cancellationToken)
    {
        await _client.AcquireTokenInteractive(_scopes).ExecuteAsync(cancellationToken);
    }


    private async Task ConfigureTokenCacheAsync()
    {
        var storageProperties = new StorageCreationPropertiesBuilder("msal_cache.dat", FileSystemHelper.GetAppDataDirectory())
            .WithMacKeyChain("ModsDudeTokenCache", "MSAL")
            .WithLinuxUnprotectedFile()
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(_client.UserTokenCache);

        _tokenCacheConfigured = true;
    }
}
