using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.WindowsClient.Model.Services;
using ModsDude.WindowsClient.Utilities.GenericFactories;
using ModsDude.WindowsClient.ViewModel.Pages;

namespace ModsDude.WindowsClient.ViewModel.Windows;
public partial class MainWindowViewModel
    : ObservableObject
{
    private readonly SessionService _sessionService;
    private readonly IFactory<MainPageViewModel> _mainPageViewModelFactory;


    public MainWindowViewModel(
        SessionService sessionService,
        IFactory<MainPageViewModel> mainPageViewModelFactory)
    {
        _sessionService = sessionService;
        _mainPageViewModelFactory = mainPageViewModelFactory;
        _sessionService.LoggedInChanged += OnSessionLoggedInChanged;
    }


    [ObservableProperty]
    private bool _loggedIn = false;

    [ObservableProperty]
    private PageViewModel _currentPage = new LoginPageViewModel();


    [RelayCommand]
    public Task Logout(CancellationToken cancellationToken)
    {
        return _sessionService.Logout(cancellationToken);
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
