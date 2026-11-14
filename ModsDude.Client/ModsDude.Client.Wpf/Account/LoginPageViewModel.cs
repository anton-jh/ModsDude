using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Wpf.Shell.Navigation;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// What is on screen until the first sign-in completes, and the way to sign in when the cached account
/// cannot be used without somebody at the keyboard.
/// </summary>
public sealed partial class LoginPageViewModel(
    IAuthenticationService authentication,
    IAccountSwitcher switcher,
    IConnectionRetry connection,
    ILogger<LoginPageViewModel> logger)
    : PageViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private LoginState _state = LoginState.SigningIn;

    public string Status => State switch
    {
        LoginState.SigningIn => "Signing in...",
        LoginState.NeedsUser => "Not signed in",
        _ => "Could not sign in"
    };


    /// <summary>
    /// Signs in as the cached account where that needs nobody, waiting out an identity provider that
    /// does not answer. Anything else leaves the page offering to sign in.
    /// </summary>
    public async Task SignInQuietlyAsync()
    {
        State = LoginState.SigningIn;

        try
        {
            var signedIn = false;

            await connection.RunAsync(
                ConnectionTarget.SignIn,
                async cancellationToken => signedIn = await authentication.TrySignInSilentlyAsync(cancellationToken),
                CancellationToken.None);

            if (signedIn is false)
            {
                State = LoginState.NeedsUser;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not sign in at startup.");

            State = LoginState.Failed;
        }
    }


    // Never greyed out while running: a sign-in tab closed without finishing never answers, and
    // clicking again is how the user gets a new one - which ends the old attempt.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SignIn(CancellationToken cancellationToken)
    {
        try
        {
            await switcher.SignInAsync(loginHint: null, cancellationToken);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            logger.LogError(exception, "Could not sign in.");

            State = LoginState.Failed;
        }
    }
}


public enum LoginState
{
    SigningIn,
    NeedsUser,
    Failed
}
