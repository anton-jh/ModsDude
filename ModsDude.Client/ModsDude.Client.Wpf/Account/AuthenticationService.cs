using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.Helpers;
using System.Windows;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// The app's one account. Tokens are only ever had silently; a browser opens only for a sign-in the
/// user asked for, through <see cref="PromptAsync"/>.
/// </summary>
/// <remarks>
/// There is no signing out. A new sign-in is prompted for and held as a <see cref="PendingSignIn"/>
/// before the current account is forgotten, so a cancelled or refused switch leaves the current user
/// exactly where they were.
/// </remarks>
public sealed class AuthenticationService : IAuthenticationService, IDisposable
{
    private const string _redirectUri = "http://localhost";
    private readonly string[] _scopes;
    private readonly IPublicClientApplication _client;
    private readonly SemaphoreSlim _tokenCacheGate = new(1, 1);
    private bool _tokenCacheConfigured;
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

    public event EventHandler? NeedsSignInChanged;


    public SignedInAccount? CurrentAccount { get; private set; }

    public bool NeedsSignIn { get; private set; }


    public async Task<string> Get(CancellationToken cancellationToken)
    {
        await EnsureTokenCacheAsync(cancellationToken);

        if (CurrentAccount is null)
        {
            throw new SignInRequiredException();
        }

        if (await FindCurrentAccountAsync() is not IAccount account)
        {
            throw RequireSignIn(null);
        }

        try
        {
            return (await _client.AcquireTokenSilent(_scopes, account).ExecuteAsync(cancellationToken)).AccessToken;
        }
        catch (MsalUiRequiredException exception)
        {
            throw RequireSignIn(exception);
        }
    }

    public async Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken)
    {
        await EnsureTokenCacheAsync(cancellationToken);

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

    /// <remarks>
    /// A browser tab closed without finishing never answers, so the attempt behind it would wait
    /// forever. Asking again is how the user says they gave up, so a new prompt ends the old one. A tab
    /// finished after being replaced reaches a listener that is no longer there.
    /// </remarks>
    public async Task<PendingSignIn?> PromptAsync(string? loginHint, CancellationToken cancellationToken)
    {
        await EnsureTokenCacheAsync(cancellationToken);

        // Called from the UI thread and resumed on it, so the one open prompt is never raced.
        using var prompt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _prompt?.Cancel();
        _prompt = prompt;

        try
        {
            var result = await AcquireTokenInteractive(loginHint, prompt.Token);

            return new PendingSignIn(result, Describe(result));
        }
        catch (MsalClientException exception) when (exception.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            return null;
        }
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

    public async Task AdoptAsync(PendingSignIn signIn)
    {
        await ForgetOtherAccountsAsync(signIn.Result.Account);

        Adopt(signIn.Result);
    }

    public async Task DiscardAsync(PendingSignIn signIn)
    {
        if (signIn.Account.Id == CurrentAccount?.Id)
        {
            return;
        }

        await _client.RemoveAsync(signIn.Result.Account);
    }


    /// <summary>
    /// Whether the sign-in library is saying the identity provider did not answer, in its own words
    /// rather than as the socket error underneath - which <see cref="Core.Connectivity.ConnectionFailure"/>
    /// already recognises wherever it is wrapped.
    /// </summary>
    public static bool IsUnreachable(Exception exception)
        => exception is MsalServiceException { ErrorCode: MsalError.RequestTimeout or MsalError.ServiceNotAvailable };


    public void Dispose()
    {
        _tokenCacheGate.Dispose();
    }


    private SignInRequiredException RequireSignIn(Exception? cause)
    {
        if (NeedsSignIn is false)
        {
            NeedsSignIn = true;
            OnUiThread(() => NeedsSignInChanged?.Invoke(this, EventArgs.Empty));
        }

        return new SignInRequiredException(cause);
    }

    /// <param name="loginHint">
    /// Where set, the sign-in page opens on that account's password rather than on a choice of
    /// accounts, and asks for it even where a session would have let it skip the page.
    /// </param>
    private Task<AuthenticationResult> AcquireTokenInteractive(string? loginHint, CancellationToken cancellationToken)
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
    /// The signed-in account by identity rather than whatever the cache lists first, so a sign-in
    /// still pending in the browser is never picked up in its place.
    /// </summary>
    private async Task<IAccount?> FindCurrentAccountAsync()
    {
        var accounts = await _client.GetAccountsAsync();

        return CurrentAccount is SignedInAccount current
            ? accounts.FirstOrDefault(x => Identify(x) == current.Id)
            : accounts.OrderBy(Identify, StringComparer.Ordinal).FirstOrDefault();
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

    private void Adopt(AuthenticationResult result)
    {
        var adopted = Describe(result);

        if (NeedsSignIn)
        {
            NeedsSignIn = false;
            OnUiThread(() => NeedsSignInChanged?.Invoke(this, EventArgs.Empty));
        }

        if (CurrentAccount?.Id == adopted.Id)
        {
            return;
        }

        CurrentAccount = adopted;

        OnUiThread(() => AccountChanged?.Invoke(this, adopted));
    }

    private static void OnUiThread(Action action)
    {
        if (Application.Current is Application app && app.CheckAccess() is false)
        {
            app.Dispatcher.Invoke(action);

            return;
        }

        action();
    }

    private async Task EnsureTokenCacheAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _tokenCacheConfigured))
        {
            return;
        }

        await _tokenCacheGate.WaitAsync(cancellationToken);

        try
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

            Volatile.Write(ref _tokenCacheConfigured, true);
        }
        finally
        {
            _tokenCacheGate.Release();
        }
    }

    private static string Identify(IAccount account)
        => account.HomeAccountId?.Identifier ?? account.Username;

    /// <summary>
    /// Named by the <c>name</c> claim - the one the server seeded its stored name from - rather than
    /// <see cref="IAccount.Username"/>, which for this tenant is the email address.
    /// </summary>
    private static SignedInAccount Describe(AuthenticationResult result)
    {
        var name = result.ClaimsPrincipal?.FindFirst("name")?.Value;

        // Matching what the server calls a user whose claim is blank.
        var displayName = string.IsNullOrWhiteSpace(name) ? "Unnamed user" : name.Trim();

        return new SignedInAccount(Identify(result.Account), displayName, result.Account.Username);
    }
}
