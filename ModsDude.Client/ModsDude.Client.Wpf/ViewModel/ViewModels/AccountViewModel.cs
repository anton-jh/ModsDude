using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.Services;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Who is signed in, and the one control over it. Switching is the whole feature - there is no
/// signing out, because an account is the only state in which the app has anything to show.
/// </summary>
/// <remarks>
/// <para>
/// A singleton, unlike the sidebar it is drawn in: the shell is rebuilt by the very transition this
/// starts, and an account panel that had to be torn down and resubscribed on each switch would be a
/// leak waiting for its first user.
/// </para>
/// <para>
/// The name comes from the token and is already right - the server stores what the identity provider
/// says and rewrites nothing. What the round trip is for is the tag and the avatar colour built from
/// it, which are worked out from the subject id on the server and are what tell this user apart from
/// the next person of the same name.
/// </para>
/// </remarks>
public partial class AccountViewModel : ObservableObject
{
    private readonly AuthenticationService _authenticationService;
    private readonly CurrentUserService _currentUserService;
    private readonly NavigationLockService _navigationLockService;
    private readonly Lazy<IModalService> _modalService;
    private readonly ILogger<AccountViewModel> _logger;


    public AccountViewModel(
        AuthenticationService authenticationService,
        CurrentUserService currentUserService,
        NavigationLockService navigationLockService,
        Lazy<IModalService> modalService,
        ILogger<AccountViewModel> logger)
    {
        _authenticationService = authenticationService;
        _currentUserService = currentUserService;
        _navigationLockService = navigationLockService;
        _modalService = modalService;
        _logger = logger;

        _displayName = Describe(authenticationService.CurrentAccount);

        _authenticationService.AccountChanged += OnAccountChanged;

        // Signing in happens before this panel exists, so the account it is being built around has
        // usually already raised its event and will not raise another one.
        if (authenticationService.CurrentAccount is not null)
        {
            _ = RefreshIdentityAsync();
        }
    }


    [ObservableProperty]
    private string _displayName;

    /// <summary>
    /// Whether this user may create repos. Null until the server has answered - the shell keeps the
    /// option open until then rather than closing one that is about to turn out to be theirs.
    /// </summary>
    [ObservableProperty]
    private bool? _isTrusted;

    /// <summary>Four digits. Not drawn beside the name here - there is only ever one user in this panel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    [NotifyPropertyChangedFor(nameof(Description))]
    private string? _tag;

    [ObservableProperty]
    private string _avatarColor = "#00000000";

    [ObservableProperty]
    private string _initial = "";

    /// <summary>False until the server has answered, so nothing is drawn in a colour that is about to change.</summary>
    public bool HasAvatar => Tag is not null;

    /// <summary>Name and tag together, for the tooltip - the one place a user can read their own tag.</summary>
    public string Description => Tag is null ? DisplayName : $"{DisplayName} {Tag}";


    [RelayCommand]
    private async Task SwitchUser(CancellationToken cancellationToken)
    {
        // Everything built from the current account is thrown away by the switch, so an editor
        // holding unsaved changes gets the same question navigating away from it would ask.
        if (_navigationLockService.HasLock() && await ConfirmDiscardAsync() is false)
        {
            return;
        }

        if (await _authenticationService.SwitchUser(cancellationToken) is false)
        {
            return;
        }

        _navigationLockService.Clear();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        var modal = new ConfirmationDialogViewModel(
            "Huh?",
            "Are you sure you want to switch user?\nThis will discard your current changes!",
            IconKind.Warning,
            "Discard changes",
            "Stay");

        await _modalService.Value.Show(modal);

        return modal.Result;
    }

    private void OnAccountChanged(object? sender, SignedInAccount account)
    {
        DisplayName = Describe(account);
        Tag = null;
        IsTrusted = null;

        _ = RefreshIdentityAsync();
    }

    private async Task RefreshIdentityAsync()
    {
        try
        {
            var user = await _currentUserService.Get(CancellationToken.None);

            DisplayName = user.DisplayName;
            AvatarColor = UserDisplay.ColorFor(user.Tag);
            Initial = UserDisplay.InitialFor(user.DisplayName);
            IsTrusted = user.IsTrusted;
            Tag = user.Tag;
        }
        catch (Exception exception)
        {
            // Swallowed on purpose. The name is already on screen and is the one the server has;
            // what is missing is decoration, and a label is not worth the app's error modal on the
            // way in. It stays out of the background-problem notice for the same reason, and lands
            // in the log so that a tag which never arrives can still be accounted for.
            _logger.LogDebug(exception, "Could not fetch the signed-in user's identity; tag and avatar colour stay unset.");
        }
    }

    partial void OnDisplayNameChanged(string value)
    {
        OnPropertyChanged(nameof(Description));
    }

    private static string Describe(SignedInAccount? account)
        => account?.DisplayName ?? "Signing in...";
}
