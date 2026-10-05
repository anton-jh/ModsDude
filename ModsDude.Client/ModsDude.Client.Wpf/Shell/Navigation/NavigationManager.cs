using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;
using System.Windows;

namespace ModsDude.Client.Wpf.Shell.Navigation;

public partial class NavigationManager(
    INavigationLockService navigationLockService,
    IModalService modalService)
    : ObservableObject, IDisposable
{
    [ObservableProperty]
    private PageViewModel? _currentPage;


    private MenuItemViewModel? _current;

    /// <summary>The entry whose page is on screen, which is a sub-page's own entry while one is open.</summary>
    public MenuItemViewModel? Current => _current;

    /// <summary>
    /// The entry the menus highlight: a sub-page's parent, otherwise the entry on screen. Setting it
    /// navigates to the entry set.
    /// </summary>
    public MenuItemViewModel? Selected
    {
        get => _current?.Parent ?? _current;
        set => Navigate(value);
    }

    public void Dispose()
    {
        if (CurrentPage is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }


    /// <summary>Leaves an open sub-page for its parent. Does nothing on any other page.</summary>
    [RelayCommand]
    private async Task GoBack()
    {
        if (_current?.Parent is MenuItemViewModel parent)
        {
            await NavigateAsync(parent);
        }
    }

    private async void Navigate(MenuItemViewModel? target)
    {
        await NavigateAsync(target);
    }

    private async Task NavigateAsync(MenuItemViewModel? target)
    {
        var previous = _current;

        if (navigationLockService.HasLock())
        {
            var confirmed = await ConfirmNavigateAwayAsync();

            if (!confirmed)
            {
                SetCurrent(previous);

                return;
            }

            navigationLockService.Clear();
        }

        if (CurrentPage is IDisposable disposable)
            disposable.Dispose();

        SetCurrent(target);

        CurrentPage = target?.GetPage();
        CurrentPage?.TriggerInit();
    }

    /// <summary>
    /// Passes through null on the way, so every menu bound to <see cref="Selected"/> lets go of
    /// whatever it picked itself before taking the entry that is now current.
    /// </summary>
    private void SetCurrent(MenuItemViewModel? value)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ReplaceCurrent(null);
            ReplaceCurrent(value);
        });
    }

    private void ReplaceCurrent(MenuItemViewModel? value)
    {
        OnPropertyChanging(nameof(Current));
        OnPropertyChanging(nameof(Selected));
        _current = value;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Selected));
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
