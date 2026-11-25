using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.ComponentModel;
using System.IO;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// The signed-in user's own page: how everybody else sees them - name and picture - and how they sign
/// in.
/// </summary>
/// <remarks>
/// The name and picture change here, immediately, for every teammate. The password and email change
/// on the server's account pages, which the buttons open in the browser.
/// </remarks>
public partial class AccountPageViewModel : PageViewModel, IDisposable
{
    /// <summary>The server's limit, repeated so the field can say so before the server has to.</summary>
    public const int MaximumNameLength = 32;

    private readonly IUserAccountService _userAccountService;
    private readonly IBrowserLauncher _browser;
    private readonly OidcClientOptions _server;
    private readonly IFilePickerService _filePickerService;
    private readonly IToastService _toasts;
    private readonly ILogger<AccountPageViewModel> _logger;


    public AccountPageViewModel(
        AccountViewModel account,
        IUserAccountService userAccountService,
        IBrowserLauncher browser,
        OidcClientOptions server,
        IFilePickerService filePickerService,
        IToastService toasts,
        ILogger<AccountPageViewModel> logger)
    {
        Account = account;
        _userAccountService = userAccountService;
        _browser = browser;
        _server = server;
        _filePickerService = filePickerService;
        _toasts = toasts;
        _logger = logger;

        _name = account.DisplayName;

        Account.PropertyChanged += OnAccountChanged;
    }


    public AccountViewModel Account { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    [NotifyCanExecuteChangedFor(nameof(SaveNameCommand))]
    private string _name;

    /// <summary>Why the name as typed would be refused, or null where it would not.</summary>
    public string? NameError => Validate(Name);

    public bool HasNameError => NameError is not null;

    /// <summary>The address the user signs in with. Null until the server has answered.</summary>
    [ObservableProperty]
    private string? _email;

    /// <summary>Whether the page is waiting on a picture being made, uploaded or removed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePictureCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemovePictureCommand))]
    private bool _isUpdatingPicture;


    public void Dispose()
    {
        Account.PropertyChanged -= OnAccountChanged;
        GC.SuppressFinalize(this);
    }


    protected override async Task InitAsync()
    {
        Email = (await _userAccountService.GetAccount(CancellationToken.None)).Email;
    }


    private bool CanSaveName()
        => NameError is null && Name.Trim() != Account.DisplayName;

    [RelayCommand(CanExecute = nameof(CanSaveName))]
    private async Task SaveName(CancellationToken cancellationToken)
    {
        var user = await _userAccountService.SetDisplayName(Name.Trim(), cancellationToken);

        Name = user.DisplayName;

        _toasts.Show($"Renamed to '{user.DisplayName}'");
    }

    private bool CanChangePicture() => IsUpdatingPicture is false;

    [RelayCommand(CanExecute = nameof(CanChangePicture))]
    private async Task ChangePicture(CancellationToken cancellationToken)
    {
        if (_filePickerService.PickImage() is not string path)
        {
            return;
        }

        IsUpdatingPicture = true;

        try
        {
            byte[] picture;

            try
            {
                picture = await Task.Run(() => AvatarPicture.Make(File.ReadAllBytes(path)), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Somebody picked a file that is not a picture, or one this machine cannot decode.
                // That is theirs to fix by picking another, so it is said plainly rather than
                // reported as the app breaking.
                _logger.LogInformation(exception, "Could not make a profile picture out of {Path}.", path);
                _toasts.Show($"Could not read '{Path.GetFileName(path)}' as a picture. Try a PNG or JPEG.", ToastSeverity.Warning);
                return;
            }

            await _userAccountService.SetAvatar(picture, AvatarPicture.ContentType, cancellationToken);
        }
        finally
        {
            IsUpdatingPicture = false;
        }
    }

    private bool CanRemovePicture() => IsUpdatingPicture is false && Account.HasPicture;

    [RelayCommand(CanExecute = nameof(CanRemovePicture))]
    private async Task RemovePicture(CancellationToken cancellationToken)
    {
        IsUpdatingPicture = true;

        try
        {
            await _userAccountService.RemoveAvatar(cancellationToken);
        }
        finally
        {
            IsUpdatingPicture = false;
        }
    }

    [RelayCommand]
    private void ChangePassword() => _browser.Open(new Uri(_server.Issuer, OidcClientOptions.ChangePasswordPath));

    [RelayCommand]
    private void ChangeEmail() => _browser.Open(new Uri(_server.Issuer, OidcClientOptions.ChangeEmailPath));


    private void OnAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountViewModel.HasPicture))
        {
            RemovePictureCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(AccountViewModel.DisplayName))
        {
            SaveNameCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The server's rules, so that a name it would refuse is never sent.</summary>
    private static string? Validate(string name)
    {
        var trimmed = name.Trim();

        if (trimmed.Length == 0)
        {
            return "Name cannot be empty";
        }

        if (trimmed.Length > MaximumNameLength)
        {
            return $"Name cannot be longer than {MaximumNameLength} characters";
        }

        if (trimmed.Any(char.IsControl))
        {
            return "Name contains characters that are not allowed";
        }

        return null;
    }
}
