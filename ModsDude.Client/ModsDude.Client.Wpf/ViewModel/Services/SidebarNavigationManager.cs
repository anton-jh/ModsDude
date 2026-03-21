using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Windows;

namespace ModsDude.Client.Wpf.Navigation;

public partial class SidebarNavigationManager(
    NavigationLockService navigationLockService,
    IModalService modalService)
    : ObservableObject, IDisposable
{
    [ObservableProperty]
    private PageViewModel? _currentPage;


    private IMenuItemViewModel? _selected;
    public IMenuItemViewModel? Selected
    {
        get => _selected;
        set
        {
            _ = HandleSelectionChangeAsync(value);
        }
    }

    public void Dispose()
    {
        if (CurrentPage is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }


    private async Task HandleSelectionChangeAsync(IMenuItemViewModel? value)
    {
        var previous = Selected;

        if (navigationLockService.HasLock())
        {
            var confirmed = await ConfirmNavigateAwayAsync();

            if (!confirmed)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanging(nameof(Selected));
                    _selected = null;
                    OnPropertyChanged(nameof(Selected));

                    OnPropertyChanging(nameof(Selected));
                    _selected = previous;
                    OnPropertyChanged(nameof(Selected));
                });

                return;
            }

            navigationLockService.Clear();
        }

        if (CurrentPage is IDisposable disposable)
            disposable.Dispose();

        Application.Current.Dispatcher.Invoke(() =>
        {
            OnPropertyChanging(nameof(Selected));
            _selected = null;
            OnPropertyChanged(nameof(Selected));

            OnPropertyChanging(nameof(Selected));
            _selected = value;
            OnPropertyChanged(nameof(Selected));
        });

        CurrentPage = value?.GetPage();
        CurrentPage?.Init();
    }

    private async Task<bool> ConfirmNavigateAwayAsync()
    {
        var modal = new ConfirmationDialogViewModel(
            "Huh?",
            "Are you sure you want to navigate away?\nThis will discard your current changes!",
            IconKind.Warning,
            "Discard changes",
            "Stay");

        await modalService.Show(modal);

        return modal.Result;
    }
}
