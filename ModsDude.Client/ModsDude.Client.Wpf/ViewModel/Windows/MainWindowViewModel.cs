using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using ModsDude.Shared.GenericFactories;

namespace ModsDude.Client.Wpf.ViewModel.Windows;
public partial class MainWindowViewModel
    : ObservableObject, IModalService
{
    private readonly IFactory<MainPageViewModel> _mainPageViewModelFactory;
    private readonly IReadOnlyList<IUserScopedState> _userScopedState;


    public MainWindowViewModel(
        AuthenticationService authService,
        IFactory<MainPageViewModel> mainPageViewModelFactory,
        IEnumerable<IUserScopedState> userScopedState,
        DriftNotificationViewModel driftNotification,
        BackgroundProblemViewModel backgroundProblems)
    {
        _mainPageViewModelFactory = mainPageViewModelFactory;
        _userScopedState = userScopedState.ToList();

        authService.AccountChanged += OnAccountChanged;

        DriftNotification = driftNotification;
        DriftNotification.Start();

        BackgroundProblems = backgroundProblems;
    }


    /// <summary>
    /// Beside the modal slot below and pointedly not in it: the drift notice has to be visible from
    /// every view without stopping the user working.
    /// </summary>
    public DriftNotificationViewModel DriftNotification { get; }

    /// <summary>
    /// Under the drift notice, and quieter: what it reports never risks anything, but it did happen
    /// and the user is entitled to know it did.
    /// </summary>
    public BackgroundProblemViewModel BackgroundProblems { get; }


    [ObservableProperty]
    private PageViewModel _currentPage = new LoginPageViewModel();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalVisible))]
    private ModalViewModel? _modal;

    public bool IsModalVisible => Modal is not null;


    /// <summary>
    /// The primary drift check runs from here, because the manifest comparison is the only mechanism
    /// that works in the normal case - the game updating mods while ModsDude is closed.
    /// </summary>
    public void NotifyWindowActivated()
    {
        DriftNotification.NotifyWindowActivated();
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

            Modal = null;

            tcs.SetResult();
        }

        modal.Completed += Handler;

        Modal = modal;

        return tcs.Task;
    }
}
