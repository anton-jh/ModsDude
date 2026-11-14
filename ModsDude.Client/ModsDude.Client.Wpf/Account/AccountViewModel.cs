using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Users;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// Who is signed in: the rail's account entry, and the state the account page edits. Switching is
/// the only thing it does to the account itself - there is no signing out, because an account is the
/// only state in which the app has anything to show.
/// </summary>
/// <remarks>
/// <para>
/// A singleton, unlike the rail it is drawn in: the shell is rebuilt by the very transition this
/// starts, and an account entry that had to be torn down and resubscribed on each switch would be a
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
    private readonly IAuthenticationService _authenticationService;
    private readonly ICurrentUserStore _currentUser;
    private readonly IAccountSwitcher _switcher;
    private readonly IUserAvatarFactory _avatarFactory;


    public AccountViewModel(
        IAuthenticationService authenticationService,
        ICurrentUserStore currentUser,
        IAccountSwitcher switcher,
        IUserAvatarFactory avatarFactory)
    {
        _authenticationService = authenticationService;
        _currentUser = currentUser;
        _switcher = switcher;
        _avatarFactory = avatarFactory;

        _displayName = Describe(authenticationService.CurrentAccount);

        _authenticationService.AccountChanged += OnAccountChanged;
        _currentUser.Changed += OnUserChanged;

        Show(_currentUser.User);
    }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private string _displayName;

    /// <summary>Whether this user may create repos. Null until the server has answered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsTrustCode))]
    private bool? _isTrusted;

    /// <summary>
    /// Only an explicit no, so a slow round trip never shows a trusted user the code box in place of
    /// what they came for.
    /// </summary>
    public bool NeedsTrustCode => IsTrusted is false;

    /// <summary>Four digits, shown in the rail's label - the one place a user can read their own tag.</summary>
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



    // Never greyed out while running: a sign-in tab closed without finishing never answers, and
    // clicking again is how the user gets a new one - which ends the old attempt.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SwitchUser(CancellationToken cancellationToken) => _switcher.SignInAsync(loginHint: null, cancellationToken);

    private void OnAccountChanged(object? sender, SignedInAccount account)
    {
        Show(_currentUser.User);
        OnPropertyChanged(nameof(Email));
    }

    private void OnUserChanged(object? sender, EventArgs e)
    {
        Show(_currentUser.User);
    }

    /// <summary>
    /// The server's answer where there is one, and the token's name with nothing else until there is.
    /// </summary>
    private void Show(CurrentUserDto? user)
    {
        DisplayName = user?.DisplayName ?? Describe(_authenticationService.CurrentAccount);
        Avatar = user is null ? null : _avatarFactory.Create(user);
        HasPicture = user?.AvatarHash is not null;
        IsTrusted = user?.IsTrusted;
        Tag = user?.Tag;
    }

    private static string Describe(SignedInAccount? account)
        => account?.DisplayName ?? "Signing in...";
}
