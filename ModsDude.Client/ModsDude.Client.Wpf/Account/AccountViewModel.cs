using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// Who is signed in: the sidebar's account card, and the state the account page edits. Switching is
/// the only thing it does to the account itself - there is no signing out, because an account is the
/// only state in which the app has anything to show.
/// </summary>
/// <remarks>
/// <para>
/// A singleton, unlike the sidebar it is drawn in: the shell is rebuilt by the very transition this
/// starts, and an account panel that had to be torn down and resubscribed on each switch would be a
/// leak waiting for its first user.
/// </para>
/// <para>
/// The name painted first is the token's, which is only right until the user renames themselves -
/// the server seeds its name from that claim once and keeps whatever the user chose after. The
/// round trip brings the stored name, the picture, and the tag and avatar colour, which are worked
/// out from the subject id on the server and are what tell this user apart from the next person of
/// the same name.
/// </para>
/// </remarks>
public partial class AccountViewModel : ObservableObject
{
    private readonly AuthenticationService _authenticationService;
    private readonly CurrentUserService _currentUserService;
    private readonly NavigationLockService _navigationLockService;
    private readonly IUserAvatarFactory _avatarFactory;
    private readonly Lazy<IModalService> _modalService;
    private readonly ILogger<AccountViewModel> _logger;


    public AccountViewModel(
        AuthenticationService authenticationService,
        CurrentUserService currentUserService,
        NavigationLockService navigationLockService,
        IUserAvatarFactory avatarFactory,
        Lazy<IModalService> modalService,
        ILogger<AccountViewModel> logger)
    {
        _authenticationService = authenticationService;
        _currentUserService = currentUserService;
        _navigationLockService = navigationLockService;
        _avatarFactory = avatarFactory;
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


    /// <summary>Raised by the card's Account button. The shell owns navigation, so it does the opening.</summary>
    public event EventHandler? OpenRequested;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private string _displayName;

    /// <summary>
    /// Whether this user may create repos. Null until the server has answered - the shell keeps the
    /// option open until then rather than closing one that is about to turn out to be theirs.
    /// </summary>
    [ObservableProperty]
    private bool? _isTrusted;

    /// <summary>Four digits. Not drawn beside the name here - there is only ever one user in this panel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private string? _tag;

    /// <summary>Null until the server has answered, so nothing is drawn in a colour that is about to change.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private AvatarViewModel? _avatar;

    /// <summary>Whether a picture is set - as opposed to loaded, which is <see cref="AvatarViewModel.HasImage"/>.</summary>
    [ObservableProperty]
    private bool _hasPicture;

    public bool HasAvatar => Avatar is not null;

    /// <summary>The address they sign in with. Only the identity provider knows it; the server never sees it.</summary>
    public string? Email => _authenticationService.CurrentAccount?.Email;

    /// <summary>Name and tag together, for the tooltip - the one place a user can read their own tag.</summary>
    public string Description => Tag is null ? DisplayName : $"{DisplayName} {Tag}";


    /// <summary>
    /// Takes the server's answer as the account's - after sign-in, and after every change the
    /// account page makes, each of which answers with the user as they now are.
    /// </summary>
    public void Apply(CurrentUserDto user)
    {
        DisplayName = user.DisplayName;
        Avatar = _avatarFactory.Create(user);
        HasPicture = user.AvatarHash is not null;
        IsTrusted = user.IsTrusted;
        Tag = user.Tag;
    }

    /// <summary>
    /// Asks the server again for the tag and colour, where the round trip at sign-in did not get them -
    /// which is what happens when the server was not answering yet.
    /// </summary>
    public Task RefreshIdentityIfMissingAsync()
        => Tag is null ? RefreshIdentityAsync() : Task.CompletedTask;


    [RelayCommand]
    private void Open()
    {
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    // Never greyed out while running: a sign-in tab closed without finishing never answers, and
    // clicking again is how the user gets a new one - which ends the old attempt.
    [RelayCommand(AllowConcurrentExecutions = true)]
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
        var modal = new ConfirmationModalViewModel(
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
        Avatar = null;
        HasPicture = false;
        IsTrusted = null;
        OnPropertyChanged(nameof(Email));

        _ = RefreshIdentityAsync();
    }

    private async Task RefreshIdentityAsync()
    {
        try
        {
            Apply(await _currentUserService.Get(CancellationToken.None));
        }
        catch (Exception exception)
        {
            // Swallowed on purpose. A name is already on screen; what is missing is decoration, and
            // a label is not worth the app's error modal on the way in. It stays out of the
            // background-problem notice for the same reason, and lands in the log so that a tag which
            // never arrives can still be accounted for.
            _logger.LogDebug(exception, "Could not fetch the signed-in user's identity; tag and avatar stay unset.");
        }
    }

    private static string Describe(SignedInAccount? account)
        => account?.DisplayName ?? "Signing in...";
}
