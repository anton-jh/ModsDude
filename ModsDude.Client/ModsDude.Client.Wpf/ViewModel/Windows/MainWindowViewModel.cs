using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.Concurrency;
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
    private readonly IResourceLeases _leases;


    public MainWindowViewModel(
        AuthenticationService authService,
        IFactory<MainPageViewModel> mainPageViewModelFactory,
        IEnumerable<IUserScopedState> userScopedState,
        NoticeCenterViewModel notices,
        BackgroundTaskViewModel backgroundTasks,
        IResourceLeases leases)
    {
        BackgroundTasks = backgroundTasks;

        _leases = leases;
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


    [ObservableProperty]
    private PageViewModel _currentPage = new LoginPageViewModel();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalVisible))]
    private ModalViewModel? _modal;

    public bool IsModalVisible => Modal is not null;


    /// <summary>
    /// Whether closing the window right now would kill work part way through, and the answer to give
    /// somebody who is about to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asked of the leases, not of the strip.</b> The strip is a report and knows only what
    /// announced itself to it; the leases are what is actually being written to, which is what closing
    /// the window would interrupt. They happen to overlap today - everything that claims also
    /// announces - and asking the wrong one would be a guard that silently stopped covering a job
    /// somebody forgot to put on screen.
    /// </para>
    /// <para>
    /// <b>A question, not a refusal.</b> An interrupted apply leaves the previous manifest standing
    /// and the folder reported as drifted, which re-applying repairs - so this is somebody's decision
    /// to make, and their reason for making it may be that the app has hung. What it must not be is a
    /// surprise.
    /// </para>
    /// </remarks>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (_leases.DescribeAll() is not { Count: > 0 } running)
        {
            return true;
        }

        var modal = new ConfirmationDialogViewModel(
            running.Count == 1 ? "Something is still running" : $"{running.Count} things are still running",
            string.Join("\n", running.Select(x => $"  {x}")) +
            "\n\nClosing now stops it part way. Nothing is lost - a mod folder left half-applied is "
            + "reported as drifted next time, and re-applying repairs it - but the work so far is wasted.",
            IconKind.Warning,
            "Close anyway",
            "Keep working");

        await Show(modal);

        return modal.Result;
    }


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

            Modal = null;

            tcs.SetResult();
        }

        modal.Completed += Handler;

        Modal = modal;

        return tcs.Task;
    }
}
