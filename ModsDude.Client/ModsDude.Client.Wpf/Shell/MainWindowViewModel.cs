using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Shell.AccountBlock;
using ModsDude.Client.Wpf.Shell.Offline;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Notices;
using ModsDude.Client.Wpf.Shell.SignIn;
using ModsDude.Client.Wpf.Shell.Toasts;
using ModsDude.Client.Wpf.Shell.Updates;

namespace ModsDude.Client.Wpf.Shell;
public partial class MainWindowViewModel
    : ObservableObject, IModalService
{
    private readonly IFactory<MainPageViewModel> _mainPageViewModelFactory;
    private readonly IReadOnlyList<IUserScopedState> _userScopedState;
    private readonly IAppLifetime _lifetime;


    public MainWindowViewModel(
        IAuthenticationService authService,
        IFactory<MainPageViewModel> mainPageViewModelFactory,
        IEnumerable<IUserScopedState> userScopedState,
        NoticeCenterViewModel notices,
        BackgroundTaskViewModel backgroundTasks,
        ToastCenterViewModel toasts,
        BuildMismatchViewModel buildMismatch,
        AccountBlockedViewModel accountBlocked,
        SignInRequiredViewModel signInRequired,
        LoginPageViewModel loginPage,
        OfflineViewModel offline,
        IAppLifetime lifetime)
    {
        BackgroundTasks = backgroundTasks;
        Toasts = toasts;
        BuildMismatch = buildMismatch;
        AccountBlocked = accountBlocked;
        SignInRequired = signInRequired;
        _currentPage = loginPage;
        Offline = offline;

        _lifetime = lifetime;
        _mainPageViewModelFactory = mainPageViewModelFactory;
        _userScopedState = userScopedState.ToList();

        authService.AccountChanged += OnAccountChanged;

        Notices = notices;
        Notices.Start();
    }


    /// <summary>
    /// The column down the right-hand side. Beside the modal slot below and pointedly not in it:
    /// every one of these has to be visible from every view without stopping the user working.
    /// </summary>
    public NoticeCenterViewModel Notices { get; }

    /// <summary>
    /// Along the top edge, and about the present rather than the past: what is running right now. It
    /// outlives the page that started the work, which is the whole reason it is here rather than on
    /// the page - an import survives navigating away from the list that queued it.
    /// </summary>
    public BackgroundTaskViewModel BackgroundTasks { get; }

    /// <summary>
    /// Along the bottom edge: what the last thing somebody did just said, for a few seconds. Above the
    /// modal layer, so a result reported while a modal is open is still seen.
    /// </summary>
    public ToastCenterViewModel Toasts { get; }

    /// <summary>Over everything but the modal layer and the toasts, while the server refuses this build.</summary>
    public BuildMismatchViewModel BuildMismatch { get; }

    /// <summary>Over everything but the modal layer and the toasts, while the server refuses the account.</summary>
    public AccountBlockedViewModel AccountBlocked { get; }

    /// <summary>Over everything but the modal layer and the toasts, while the account has to sign in again.</summary>
    public SignInRequiredViewModel SignInRequired { get; }

    /// <summary>Over everything but the progress strip, the modal layer and the toasts, while the server does not answer.</summary>
    public OfflineViewModel Offline { get; }


    [ObservableProperty]
    private PageViewModel _currentPage;

    /// <summary>Every open modal, oldest first. Only the newest is drawn.</summary>
    private readonly List<ModalViewModel> _modals = [];

    public ModalViewModel? Modal => _modals.LastOrDefault();

    public bool IsModalVisible => Modal is not null;

    /// <summary>The modal's corner close button and a click on the scrim around it.</summary>
    [RelayCommand]
    private void DismissModal() => Modal?.TryDismiss();

    /// <summary>The window's close button. Leaving goes through the one exit every way out uses.</summary>
    [RelayCommand]
    private Task Quit() => _lifetime.QuitAsync();


    /// <summary>
    /// The primary drift check runs from here, because the manifest comparison is the only mechanism
    /// that works in the normal case - the game updating mods while ModsDude is closed.
    /// </summary>
    public void NotifyWindowActivated()
    {
        Notices.NotifyWindowActivated();
    }


    /// <summary>
    /// Signing in and switching to somebody else are the same transition: the shell and everything
    /// under it was built from the previous account, so it goes, and a new one is built from
    /// scratch. <see cref="ShellNavigationService"/> exists because of this - a shell that is
    /// replaced cannot be handed out at composition time.
    /// </summary>
    private void OnAccountChanged(object? sender, SignedInAccount account)
    {
        (CurrentPage as IDisposable)?.Dispose();

        // Before the new shell is built, so it never draws the previous account's repos on its way
        // to a refresh that would have removed them.
        foreach (var state in _userScopedState)
        {
            state.ClearUserState();
        }

        CurrentPage = _mainPageViewModelFactory.Create();
        CurrentPage.TriggerInit();
    }

    public Task Show(ModalViewModel modal)
    {
        var tcs = new TaskCompletionSource();

        void Handler()
        {
            modal.Completed -= Handler;

            _modals.Remove(modal);
            OnModalsChanged();

            tcs.SetResult();
        }

        modal.Completed += Handler;

        _modals.Add(modal);
        OnModalsChanged();

        return tcs.Task;
    }

    public void DismissAll()
    {
        foreach (var modal in Enumerable.Reverse(_modals).ToList())
        {
            modal.Abandon();
        }
    }

    private void OnModalsChanged()
    {
        OnPropertyChanged(nameof(Modal));
        OnPropertyChanged(nameof(IsModalVisible));
    }
}
