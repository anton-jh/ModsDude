using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.Navigation;

public partial class SidebarNavigationManager
    : ObservableObject, IDisposable
{
    [ObservableProperty]
    private PageViewModel? _currentPage;


    public IMenuItemViewModel? Selected
    {
        get => field;
        set
        {
            if (CurrentPage is IDisposable disposable)
            {
                disposable.Dispose();
            }

            OnPropertyChanging();
            field = null;
            OnPropertyChanged();

            OnPropertyChanging();
            field = value;
            OnPropertyChanged();

            CurrentPage = field?.GetPage();
            CurrentPage?.Init();
        }
    }

    public void Dispose()
    {
        if (CurrentPage is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
