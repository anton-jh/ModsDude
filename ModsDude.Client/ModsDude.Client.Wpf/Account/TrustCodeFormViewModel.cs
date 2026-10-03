using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// The code box that makes the user trusted, offered wherever creating a repo is.
/// </summary>
public partial class TrustCodeFormViewModel(
    ITrustCodeService trustCodeService,
    AccountViewModel account,
    IToastService toasts,
    ILogger<TrustCodeFormViewModel> logger)
    : ObservableObject
{
    /// <summary>Taken as typed. The server accepts any casing and spacing.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedeemCommand))]
    private string _code = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    public bool CanRedeem => !string.IsNullOrWhiteSpace(Code);


    [RelayCommand(CanExecute = nameof(CanRedeem))]
    private async Task Redeem(CancellationToken cancellationToken)
    {
        CurrentUserDto user;

        try
        {
            user = await trustCodeService.Redeem(Code, cancellationToken);
        }
        catch (UserFriendlyException exception)
        {
            logger.LogInformation("Trust code not redeemed: {Reason}", exception.DeveloperMessage);
            Error = exception.UserMessage;
            return;
        }

        Code = "";
        account.Apply(user);
        toasts.Show("You can now create repos.");
    }


    partial void OnCodeChanged(string value)
    {
        Error = null;
    }
}
