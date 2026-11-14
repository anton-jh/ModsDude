using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Shell.SignIn;

/// <summary>
/// Covers the whole window while the signed-in account's session has run out, because nothing that
/// needs the server works until the user signs in again.
/// </summary>
public sealed partial class SignInRequiredViewModel : ObservableObject, IDisposable
{
    private readonly IAuthenticationService _authentication;
    private readonly IAccountSwitcher _switcher;
    private readonly IConnectionRetry _connection;
    private readonly IErrorReporter _errorReporter;
    private readonly ILogger<SignInRequiredViewModel> _logger;


    public SignInRequiredViewModel(
        IAuthenticationService authentication,
        IAccountSwitcher switcher,
        IConnectionRetry connection,
        IErrorReporter errorReporter,
        AccountViewModel account,
        ILogger<SignInRequiredViewModel> logger)
    {
        _authentication = authentication;
        _switcher = switcher;
        _connection = connection;
        _errorReporter = errorReporter;
        _logger = logger;
        Account = account;

        _authentication.NeedsSignInChanged += OnChanged;
        _authentication.AccountChanged += OnAccountChanged;
    }


    public bool IsVisible => _authentication.NeedsSignIn && _authentication.CurrentAccount is not null;

    public AccountViewModel Account { get; }


    // Never greyed out while running: a sign-in tab closed without finishing never answers, and
    // clicking again is how the user gets a new one - which ends the old attempt.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SignIn(CancellationToken cancellationToken)
    {
        try
        {
            if (await _switcher.SignInAsync(_authentication.CurrentAccount?.Email, cancellationToken))
            {
                // Whatever was waiting for a token tries again now rather than at its next interval.
                _connection.RetryNow();
            }
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            _logger.LogError(exception, "Could not sign in again.");

            await _errorReporter.ShowAsync(exception, "signing in again");
        }
    }


    public void Dispose()
    {
        _authentication.NeedsSignInChanged -= OnChanged;
        _authentication.AccountChanged -= OnAccountChanged;
    }


    private void OnChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(IsVisible));

    private void OnAccountChanged(object? sender, SignedInAccount e) => OnPropertyChanged(nameof(IsVisible));
}
