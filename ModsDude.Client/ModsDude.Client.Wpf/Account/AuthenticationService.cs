using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.Helpers;
using System.Windows;

namespace ModsDude.Client.Wpf.Account;

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
public class AuthenticationService : IAuthenticationService
{
    private const string _redirectUri = "http://localhost";
    private readonly string[] _scopes;
    private readonly IPublicClientApplication _client;
    private bool _tokenCacheConfigured = false;
    private CancellationTokenSource? _prompt;


    public AuthenticationService(AuthenticationOptions options)
    {
        _scopes = [options.Scope, "openid", "offline_access"];
        _client = PublicClientApplicationBuilder
            .Create(options.ClientId)
            .WithAuthority(options.Authority)
            .WithRedirectUri(_redirectUri)
            .Build();
    }


    public event EventHandler<SignedInAccount>? AccountChanged;


    public SignedInAccount? CurrentAccount { get; private set; }


    public async Task<string> Get(CancellationToken cancellationToken)
    {
        await EnsureTokenCacheAsync();

        var result = await AcquireAsync(cancellationToken);

        Adopt(result);

        return result.AccessToken;
    }

    public async Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken)
    {
        await EnsureTokenCacheAsync();

        if (await FindCurrentAccountAsync() is not IAccount account)
        {
            return false;
        }

        try
        {
            Adopt(await _client.AcquireTokenSilent(_scopes, account).ExecuteAsync(cancellationToken));

            return true;
        }
        catch (MsalUiRequiredException)
        {
            return false;
        }
    }

    public async Task<bool> SwitchUser(CancellationToken cancellationToken)
    {
        await EnsureTokenCacheAsync();

        if (await PromptAsync(cancellationToken) is not AuthenticationResult result)
        {
            return false;
        }

        // Only now that there is somebody to replace them with. Clearing the cache on the way in
        // would turn every cancelled switch into an accidental sign-out.
        await ForgetOtherAccountsAsync(result.Account);

        return Adopt(result);
    }

    public async Task<bool> ResetPassword(CancellationToken cancellationToken)
    {
        if (CurrentAccount is not SignedInAccount current)
        {
            return false;
        }

        await EnsureTokenCacheAsync();

        if (await PromptAsync(cancellationToken, current.Email) is not AuthenticationResult result)
        {
            return false;
        }

        await ForgetOtherAccountsAsync(result.Account);

        Adopt(result);

        return true;
    }


    /// <summary>
    /// Whether the sign-in library is saying the identity provider did not answer, in its own words
    /// rather than as the socket error underneath - which <see cref="Core.Connectivity.ConnectionFailure"/>
    /// already recognises wherever it is wrapped.
    /// </summary>
    public static bool IsUnreachable(Exception exception)
        => exception is MsalServiceException { ErrorCode: MsalError.RequestTimeout or MsalError.ServiceNotAvailable };


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

        return await AcquireTokenInteractive(cancellationToken);
    }

    /// <summary>
    /// A sign-in page the user asked for, which replaces any such page still open.
    /// </summary>
    /// <remarks>
    /// A browser tab closed without finishing never answers, so the attempt behind it would wait
    /// forever - and one finished much later would still sign somebody in, long after the user gave up
    /// on it. Asking again is how the user says they gave up, so that is what ends the old attempt. A
    /// tab finished after being replaced reaches a listener that is no longer there.
    /// </remarks>
    /// <returns>Null where the user closed the page, asked for a new one before finishing it, or the caller cancelled.</returns>
    private async Task<AuthenticationResult?> PromptAsync(CancellationToken cancellationToken, string? loginHint = null)
    {
        // Called from the UI thread and resumed on it, so the one open prompt is never raced.
        using var prompt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _prompt?.Cancel();
        _prompt = prompt;

        try
        {
            return await AcquireTokenInteractive(prompt.Token, loginHint);
        }
        catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            return null;
        }
        // Whoever cancelled it - this, for a newer prompt, or the command itself, which cancels its
        // previous run when clicked again - the user did not finish the page, and that is all it means.
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (_prompt == prompt)
            {
                _prompt = null;
            }
        }
    }

    /// <param name="loginHint">
    /// Where set, the sign-in page opens on that account's password rather than on a choice of
    /// accounts, and asks for it even where a session would have let it skip the page.
    /// </param>
    private Task<AuthenticationResult> AcquireTokenInteractive(CancellationToken cancellationToken, string? loginHint = null)
    {
        var builder = _client.AcquireTokenInteractive(_scopes);

        builder = loginHint is null
            ? builder.WithPrompt(Prompt.SelectAccount)
            : builder.WithLoginHint(loginHint).WithPrompt(Prompt.ForceLogin);

        return builder
                    .WithSystemWebViewOptions(new SystemWebViewOptions
                    {
                        HtmlMessageSuccess = """
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <meta charset="utf-8">
                            <meta name="color-scheme" content="dark">
                            <style>
                                :root {
                                    color-scheme: dark;
                                }

                                html, body {
                                    margin: 0;
                                    min-height: 100%;
                                    background: #1e1e1e;
                                    color: #d4d4d4;
                                    font-family: system-ui, sans-serif;
                                }

                                body {
                                    display: grid;
                                    place-items: center;
                                    min-height: 100vh;
                                    text-align: center;
                                }
                            </style>
                        </head>
                        <body>
                            <div>
                                <h3>You're signed in.</h3>
                                <p>You can return to ModsDude and close this tab.</p>
                            </div>
                        </body>
                        </html>
                        """
                    })
                    .ExecuteAsync(cancellationToken);
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
        var adopted = new SignedInAccount(Identify(result.Account), Describe(result), result.Account.Username);

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
    /// This is the claim the server seeded its stored name from, so it is the right thing to paint
    /// immediately and the same as the stored name until the user first renames themselves.
    /// <see cref="AccountViewModel"/> replaces it with the server's once that has
    /// been asked for.
    /// </remarks>
    private static string Describe(AuthenticationResult result)
    {
        var name = result.ClaimsPrincipal?.FindFirst("name")?.Value;

        // Matching what the server calls a user whose claim is blank, so that the placeholder is not
        // one more name than the system actually has.
        return string.IsNullOrWhiteSpace(name) ? "Unnamed user" : name.Trim();
    }
}
