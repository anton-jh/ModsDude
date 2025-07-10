using Microsoft.Identity.Client;
using ModsDude.Client.Core.Authentication;

namespace ModsDude.Client.Cli.Authentication;

internal class AuthenticationService : IAccessTokenAccessor
{
    private const string _clientId = "17e5db7c-9023-40cd-9cd8-3c49b7f98927";
    private static readonly string _authority = "https://modsdudeexternal.ciamlogin.com/cce54c8f-87a3-4c39-a558-9a15733d2cdf/susi_1/v2.0";
    private static readonly string _redirectUri = "http://localhost";
    private static readonly string[] _scopes = ["api://modsdude-server/act_as_user", "openid", "offline_access"];
    private readonly IPublicClientApplication _client;


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
        AuthenticationResult? result = null;

        var accounts = (await _client.GetAccountsAsync()).ToList();

        if (accounts.Any())
        {
            try
            {
                result = await _client.AcquireTokenSilent(_scopes, accounts.First()).ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException)
            {

            }
            catch (Exception ex)
            {
                throw;
            }
        }

        if (result is null)
        {
            try
            {
                result = await _client.AcquireTokenInteractive(_scopes).ExecuteAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        return result.AccessToken;
    }
}
