using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;
using System.Windows;

namespace ModsDude.Client.Wpf.Shell.Navigation;

public partial class NavigationManager(
    NavigationLockService navigationLockService,
    IModalService modalService)
    : ObservableObject, IDisposable
{
    [ObservableProperty]
    private PageViewModel? _currentPage;


    private MenuItemViewModel? _selected;
    public MenuItemViewModel? Selected
    {
        get => _selected;
        set
        {
            HandleSelectionChangeAsync(value);
        }
    }

    public void Dispose()
    {
        if (CurrentPage is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }


    private async void HandleSelectionChangeAsync(MenuItemViewModel? value)
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
        CurrentPage?.TriggerInit();
    }

    private async Task<bool> ConfirmNavigateAwayAsync()
    {
        var modal = new ConfirmationModalViewModel(
            "Huh?",
            "Are you sure you want to navigate away?\nThis will discard your current changes!",
            IconKind.Warning,
            "Discard changes",
            "Stay");

        await modalService.Show(modal);

        return modal.Result;
    }
}
