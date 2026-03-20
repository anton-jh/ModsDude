using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Shared.GenericFactories;

namespace ModsDude.Client.Wpf.ViewModel.Windows;
public partial class MainWindowViewModel
    : ObservableObject
{
    private readonly AuthenticationService _authService;
    private readonly IFactory<MainPageViewModel> _mainPageViewModelFactory;


    public MainWindowViewModel(
        AuthenticationService authService,
        IFactory<MainPageViewModel> mainPageViewModelFactory)
    {
        _authService = authService;
        _mainPageViewModelFactory = mainPageViewModelFactory;
        _authService.LoggedInChanged += OnSessionLoggedInChanged;
    }


    [ObservableProperty]
    private bool _loggedIn = false;

    [ObservableProperty]
    private PageViewModel _currentPage = new LoginPageViewModel();


    [RelayCommand]
    public Task Logout(CancellationToken cancellationToken)
    {
        return _authService.ForceRelogin(cancellationToken);
    }


    private void OnSessionLoggedInChanged(object? sender, bool e)
    {
        LoggedIn = e;
    }


    partial void OnLoggedInChanged(bool value)
    {
        CurrentPage = value
            ? _mainPageViewModelFactory.Create()
            : new LoginPageViewModel();
        CurrentPage.Init();
    }
}
