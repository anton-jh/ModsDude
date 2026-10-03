using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Accounts;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Shell.Modals;
using System.Windows;

namespace ModsDude.Client.Wpf.Shell.AccountBlock;

/// <summary>
/// Covers the whole window while the server refuses the signed-in account, because nothing that
/// needs the server works until it is unblocked.
/// </summary>
public sealed partial class AccountBlockedViewModel : ObservableObject, IDisposable
{
    private readonly IAccountStatus _status;
    private readonly IConnectionRetry _connection;
    private readonly ICurrentUserService _currentUser;
    private readonly IErrorReporter _errorReporter;
    private readonly ILogger<AccountBlockedViewModel> _logger;


    public AccountBlockedViewModel(
        IAccountStatus status,
        IConnectionRetry connection,
        ICurrentUserService currentUser,
        IErrorReporter errorReporter,
        AccountViewModel account,
        ILogger<AccountBlockedViewModel> logger)
    {
        _status = status;
        _connection = connection;
        _currentUser = currentUser;
        _errorReporter = errorReporter;
        _logger = logger;
        Account = account;

        _status.Changed += OnChanged;
    }


    public bool IsVisible => _status.IsBlocked;

    public AccountViewModel Account { get; }


    /// <summary>
    /// Asks the server again. Any answer it accepts clears the block, so this works whether or not
    /// anything else is still trying.
    /// </summary>
    [RelayCommand]
    private async Task Retry(CancellationToken cancellationToken)
    {
        _connection.RetryNow();

        try
        {
            await _currentUser.Get(cancellationToken);
        }
        catch (Exception exception) when (ConnectionFailure.Is(exception))
        {
            _logger.LogInformation(exception, "The server still refuses the account, or did not answer.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await _errorReporter.ShowAsync(exception, "checking whether the account is still blocked");
        }
    }


    public void Dispose()
    {
        _status.Changed -= OnChanged;
    }


    private void OnChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() => OnPropertyChanged(nameof(IsVisible)));
    }
}
