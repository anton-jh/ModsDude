using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Desktop;
using Microsoft.Identity.Client.Extensions.Msal;
using ModsDude.WindowsClient.Model.Helpers;
using ModsDude.WindowsClient.Model.Interfaces;
using ModsDude.WindowsClient.Model.Models;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModsDude.WindowsClient.Wpf.Auth;
internal class AadB2CAuthService : IAuthService
{
    private const string _cacheFilename = "token_cache";

    private static readonly string _tenantName = "modsdude";
    private static readonly string _tenant = $"{_tenantName}.onmicrosoft.com";
    private static readonly string _azureAdB2CHostname = $"{_tenantName}.b2clogin.com";
    private static readonly string _clientId = "6ef1be23-6842-4764-a341-9228af7ade41";
    private static readonly string _redirectUri = $"https://{_tenantName}.b2clogin.com/oauth2/nativeclient";
    private static readonly string _policySignUpSignIn = "B2C_1_signupsignin1";
    //private static readonly string _policyEditProfile = "b2c_1_edit_profile";
    private static readonly string _policyResetPassword = "b2c_1_reset";
    private static readonly string[] _apiScopes = ["offline_access", "openid", $"https://{_tenant}/modsdude-server/default"];
    private static readonly string _authorityBase = $"https://{_azureAdB2CHostname}/tfp/{_tenant}/";
    private static readonly string _authoritySignUpSignIn = $"{_authorityBase}{_policySignUpSignIn}";
    //private static readonly string _authorityEditProfile = $"{_authorityBase}{_policyEditProfile}";
    private static readonly string _authorityResetPassword = $"{_authorityBase}{_policyResetPassword}";

    private IPublicClientApplication _publicClientApp;
    private bool _tokenCacheConfigured = false;


    public AadB2CAuthService()
    {
        _publicClientApp = PublicClientApplicationBuilder.Create(_clientId)
            .WithB2CAuthority(_authoritySignUpSignIn)
            .WithRedirectUri(_redirectUri)
            .WithWindowsEmbeddedBrowserSupport()
            .Build();
    }

    public async Task<Session> GetSession(CancellationToken cancellationToken)
    {
        if (!_tokenCacheConfigured)
        {
            await ConfigureTokenCacheAsync();
        }

        AuthenticationResult authResult;    

        var accounts = await _publicClientApp.GetAccountsAsync();

        try
        {
            authResult = await _publicClientApp.AcquireTokenSilent(_apiScopes, accounts.FirstOrDefault())
                .ExecuteAsync(cancellationToken);
        }
        catch (MsalUiRequiredException)
        {
            authResult = await _publicClientApp.AcquireTokenInteractive(_apiScopes)
                .WithUseEmbeddedWebView(true)
                .WithAccount(accounts.FirstOrDefault())
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(cancellationToken);
        }

        return new()
        {
            AccessToken = authResult.AccessToken,
            UserId = authResult.UniqueId
        };
    }

    public async Task Logout(CancellationToken cancellationToken)
    {
        var accounts = await _publicClientApp.GetAccountsAsync();

        if (accounts.Any())
        {
            await _publicClientApp.RemoveAsync(accounts.FirstOrDefault());
        }
    }

    private async Task ConfigureTokenCacheAsync()
    {
        var storageProperties = new StorageCreationPropertiesBuilder("msal_cache.dat", FileSystemHelper.GetAppDataDirectory())
            .WithMacKeyChain("ModsDudeTokenCache", "MSAL")
            .WithLinuxUnprotectedFile()
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(_publicClientApp.UserTokenCache);

        _tokenCacheConfigured = true;
    }
}
