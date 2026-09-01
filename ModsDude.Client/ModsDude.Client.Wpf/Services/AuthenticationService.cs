using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.Helpers;
using System.Windows;

namespace ModsDude.Client.Wpf.Services;

/// <summary>
/// The app's one account, and the only control over it.
/// </summary>
/// <remarks>
/// There is no signing out. Every surface in ModsDude is a server call, so a signed-out app is an
/// app with nothing to show; the thing users actually want is to be somebody else, which is
/// <see cref="SwitchUser"/>. It signs the new user in <i>before</i> forgetting the old one, so a
/// cancelled switch - or a failed one - leaves the current user exactly where they were rather than
/// stranding the app in a state it has no page for.
/// </remarks>
public class AuthenticationService : IAccessTokenAccessor
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


    /// <summary>
    /// Raised when the signed-in account becomes a different one - the first sign-in, or a switch.
    /// Always on the UI thread: MSAL finishes wherever it likes, and everything listening to this
    /// rebuilds bound state.
    /// </summary>
    public event EventHandler<SignedInAccount>? AccountChanged;


    /// <summary>Null until the first sign-in completes, and never null again.</summary>
    public SignedInAccount? CurrentAccount { get; private set; }


    public async Task<string> Get(CancellationToken cancellationToken)
    {
        await EnsureTokenCacheAsync();

        var result = await AcquireAsync(cancellationToken);

        Adopt(result);

        return result.AccessToken;
    }

    /// <summary>
    /// Prompts for an account and signs in as whoever is picked.
    /// </summary>
    /// <returns>
    /// False where the user cancelled the prompt, or picked the account they were already on. Either
    /// way nothing changed and no event was raised.
    /// </returns>
    public async Task<bool> SwitchUser(CancellationToken cancellationToken)
    {
        await EnsureTokenCacheAsync();

        AuthenticationResult result;

        try
        {
            result = await _client
                .AcquireTokenInteractive(_scopes)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(cancellationToken);
        }
        catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            return false;
        }

        // Only now that there is somebody to replace them with. Clearing the cache on the way in
        // would turn every cancelled switch into an accidental sign-out.
        await ForgetOtherAccountsAsync(result.Account);

        return Adopt(result);
    }


    private async Task<AuthenticationResult> AcquireAsync(CancellationToken cancellationToken)
    {
        var account = await FindCurrentAccountAsync();

        if (account is not null)
        {
            try
            {
                return await _client.AcquireTokenSilent(_scopes, account).ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException)
            {
            }
        }

        return await _client.AcquireTokenInteractive(_scopes).ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// The signed-in account by identity, rather than whatever the cache happens to list first. A
    /// completed switch leaves exactly one account behind, but a token acquisition racing one that is
    /// still in the browser must not pick up the account being replaced.
    /// </summary>
    private async Task<IAccount?> FindCurrentAccountAsync()
    {
        var accounts = await _client.GetAccountsAsync();

        return CurrentAccount is SignedInAccount current
            ? accounts.FirstOrDefault(x => Identify(x) == current.Id)
            : accounts.FirstOrDefault();
    }

    private async Task ForgetOtherAccountsAsync(IAccount kept)
    {
        foreach (var account in await _client.GetAccountsAsync())
        {
            if (Identify(account) != Identify(kept))
            {
                await _client.RemoveAsync(account);
            }
        }
    }

    /// <returns>True where this is a different user from the one signed in a moment ago.</returns>
    private bool Adopt(AuthenticationResult result)
    {
        var adopted = new SignedInAccount(Identify(result.Account), Describe(result));

        // Get() runs on every outgoing request, so the common case here is the same account again.
        if (CurrentAccount?.Id == adopted.Id)
        {
            return false;
        }

        CurrentAccount = adopted;

        RaiseAccountChanged(adopted);

        return true;
    }

    private void RaiseAccountChanged(SignedInAccount account)
    {
        if (AccountChanged is not EventHandler<SignedInAccount> handler)
        {
            return;
        }

        if (Application.Current is Application app && app.CheckAccess() is false)
        {
            _ = app.Dispatcher.InvokeAsync(() => handler(this, account));

            return;
        }

        handler(this, account);
    }

    private async Task EnsureTokenCacheAsync()
    {
        if (_tokenCacheConfigured)
        {
            return;
        }

        var storageProperties = new StorageCreationPropertiesBuilder("msal_cache.dat", FileSystemHelper.GetAppDataDirectory())
            .WithMacKeyChain("ModsDudeTokenCache", "MSAL")
            .WithLinuxUnprotectedFile()
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(_client.UserTokenCache);

        _tokenCacheConfigured = true;
    }

    private static string Identify(IAccount account)
        => account.HomeAccountId?.Identifier ?? account.Username;

    /// <summary>
    /// The <c>name</c> claim - deliberately not <see cref="IAccount.Username"/>, which despite the
    /// word is the account's <i>identifier</i> at the provider and for this tenant is the email
    /// address they sign in with, not a name anybody chose.
    /// </summary>
    /// <remarks>
    /// This is the same claim the server derives its stored username from, so it is the right thing
    /// to paint immediately and identical to the authoritative answer unless that name was already
    /// taken. <see cref="ViewModel.ViewModels.AccountViewModel"/> replaces it with the server's once
    /// that has been asked for.
    /// </remarks>
    private static string Describe(AuthenticationResult result)
    {
        var name = result.ClaimsPrincipal?.FindFirst("name")?.Value;

        // Matching what the server calls a user whose claim is blank, so that the placeholder is not
        // one more name than the system actually has.
        return string.IsNullOrWhiteSpace(name) ? "Unnamed user" : name.Trim();
    }
}
