using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Wpf.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Windows;

namespace ModsDude.Client.Wpf.Navigation;

public partial class SidebarNavigationManager(NavigationLockService navigationLockService)
    : ObservableObject, IDisposable
{
    [ObservableProperty]
    private PageViewModel? _currentPage;


    public IMenuItemViewModel? Selected
    {
        get => field;
        set
        {
            if (navigationLockService.HasLock())
            {
                if (ConfirmNavigateAway())
                {
                    navigationLockService.Clear();
                }
                else
                {
                    var previous = field;

                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        OnPropertyChanging();
                        field = null;
                        OnPropertyChanged();

                        OnPropertyChanging();
                        field = previous;
                        OnPropertyChanged();
                    });

                    return;
                }
            }

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


    private static bool ConfirmNavigateAway()
    {
        var result = System.Windows.Forms.MessageBox.Show(
            "Are you sure you want to navigate away?\nThis will discard your current changes!",
            "Huh?",
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Warning,
            System.Windows.Forms.MessageBoxDefaultButton.Button2);

        return result is System.Windows.Forms.DialogResult.Yes;
    }
}
