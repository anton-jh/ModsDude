using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;
using System.Windows;

namespace ModsDude.Client.Wpf.Shell.Navigation;

public partial class NavigationManager(
    INavigationLockService navigationLockService,
    IRetainedPages retainedPages,
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
        set => Select(value);
    }

    /// <summary>
    /// Builds the innermost page on screen again, so it reads everything afresh. Left alone while it
    /// holds changes nobody has saved.
    /// </summary>
    public void ReloadInnermost()
    {
        if (CurrentPage is INavigationHost host)
        {
            host.NavManager.ReloadInnermost();

            return;
        }

        if (_current is null || navigationLockService.HasLock())
        {
            return;
        }

        retainedPages.Release(CurrentPage);

        Show(_current.GetPage());
    }

    public void Dispose()
    {
        retainedPages.Release(CurrentPage);
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

    /// <summary>
    /// A menu's own pick, run as a command so its failure reaches the error modal. A pick made while a
    /// navigation is still asking about unsaved changes is pushed back to what is on screen.
    /// </summary>
    private void Select(MenuItemViewModel? target)
    {
        if (NavigateCommand.CanExecute(target) is false)
        {
            SetCurrent(_current);

            return;
        }

        NavigateCommand.Execute(target);
    }

    [RelayCommand]
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

        retainedPages.Release(CurrentPage);

        SetCurrent(target);

        Show(target?.GetPage());
    }

    /// <summary>Puts a page on screen: a new one, or a kept one its menu entry took back.</summary>
    private void Show(PageViewModel? page)
    {
        CurrentPage = page;

        if (page is not null)
        {
            retainedPages.Show(page);
            page.TriggerInit();
        }
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
        var modal = ConfirmationModalViewModel.ConfirmDiscardChanges();

        await modalService.Show(modal);

        return modal.Result;
    }
}
