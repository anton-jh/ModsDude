using CommunityToolkit.Mvvm.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public abstract class PageViewModel : ObservableObject
{
    public void TriggerInit()
    {
        // Quick, UI-related initialization on the UI thread.
        _ = Application.Current.Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                try
                {
                    Init();
                }
                catch (Exception ex)
                {
                    OnInitFailed(ex);
                }
            }),
            DispatcherPriority.Loaded);

        // Async initialization off the UI thread so blocking IO doesn't freeze the app.
        _ = Task.Run(async () =>
        {
            try
            {
                await InitAsync().ConfigureAwait(false);

                _ = Application.Current.Dispatcher.BeginInvoke((Action)OnInitCompleted, DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                _ = Application.Current.Dispatcher.BeginInvoke((Action)(() => OnInitFailed(ex)), DispatcherPriority.Normal);
            }
        });
    }

    /// <summary>
    /// Called on the UI thread after <see cref="InitAsync"/> completes successfully.
    /// </summary>
    protected virtual void OnInitCompleted() { }

    /// <summary>
    /// Called on the UI thread if initialization (sync or async) throws. The default rethrows on the
    /// dispatcher so the global handler in App.xaml.cs shows the error modal. Override to handle it
    /// locally instead - overrides that don't rethrow suppress the modal.
    /// </summary>
    protected virtual void OnInitFailed(Exception ex)
        => ExceptionDispatchInfo.Capture(ex).Throw();

    protected virtual void Init() { }
    protected virtual Task InitAsync() => Task.CompletedTask;
}
