using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.ComponentModel;
using System.IO;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// The signed-in user's own page: how everybody else sees them - name and picture - and the two
/// things that are about the account itself rather than this system, the password and who is
/// signed in.
/// </summary>
/// <remarks>
/// The name and picture are this system's and change here, immediately, for every teammate. The
/// password belongs to the identity provider, which has no page for changing one while signed in;
/// what it offers is a reset by emailed code from its sign-in page, so that is where the button
/// goes.
/// </remarks>
public partial class AccountPageViewModel : PageViewModel, IDisposable
{
    /// <summary>The server's limit, repeated so the field can say so before the server has to.</summary>
    public const int MaximumNameLength = 32;

    private readonly UserAccountService _userAccountService;
    private readonly AuthenticationService _authenticationService;
    private readonly IFilePickerService _filePickerService;
    private readonly IToastService _toasts;
    private readonly ILogger<AccountPageViewModel> _logger;


    public AccountPageViewModel(
        AccountViewModel account,
        UserAccountService userAccountService,
        AuthenticationService authenticationService,
        IFilePickerService filePickerService,
        IToastService toasts,
        ILogger<AccountPageViewModel> logger)
    {
        Account = account;
        _userAccountService = userAccountService;
        _authenticationService = authenticationService;
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


    private bool CanSaveName()
        => NameError is null && Name.Trim() != Account.DisplayName;

    [RelayCommand(CanExecute = nameof(CanSaveName))]
    private async Task SaveName(CancellationToken cancellationToken)
    {
        var user = await _userAccountService.SetDisplayName(Name.Trim(), cancellationToken);

        Account.Apply(user);
        Name = user.DisplayName;

        _toasts.Show($"You are now called {user.DisplayName}.");
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
                _toasts.Show($"'{Path.GetFileName(path)}' could not be read as a picture. Try a PNG or JPEG.", ToastSeverity.Warning);
                return;
            }

            Account.Apply(await _userAccountService.SetAvatar(picture, AvatarPicture.ContentType, cancellationToken));
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
            Account.Apply(await _userAccountService.RemoveAvatar(cancellationToken));
        }
        finally
        {
            IsUpdatingPicture = false;
        }
    }

    // Never greyed out while running, for the same reason as switching user: a closed tab never
    // answers, and asking again replaces it.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ResetPassword(CancellationToken cancellationToken)
    {
        if (await _authenticationService.ResetPassword(cancellationToken))
        {
            _toasts.Show("Signed in again. If you set a new password, it is the one to use from now on.");
        }
    }


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
            return "Your name cannot be empty.";
        }

        if (trimmed.Length > MaximumNameLength)
        {
            return $"Your name cannot be longer than {MaximumNameLength} characters.";
        }

        if (trimmed.Any(char.IsControl))
        {
            return "Your name cannot contain control characters.";
        }

        return null;
    }
}
