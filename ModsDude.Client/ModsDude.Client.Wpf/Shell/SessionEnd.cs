using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Sessions;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Shell;

public sealed class SessionEnd(
    IResourceLeases leases,
    INavigationLockService navigationLock,
    IRetainedPages retainedPages,
    ISessionWork session,
    IBackgroundTaskReporter backgroundTasks,
    Lazy<MainWindow> window,
    Lazy<IModalService> modalService) : ISessionEnd
{
    public IReadOnlyList<string> DescribeAtStake()
    {
        IEnumerable<string> unsaved = navigationLock.HasLock() ? ["Unsaved changes"] : [];

        return [.. leases.DescribeAll().Order(StringComparer.Ordinal).Concat(retainedPages.DescribeAtStake()).Concat(unsaved)];
    }

    public async Task<bool> ConfirmAsync(string title, string confirmText)
    {
        if (DescribeAtStake() is not { Count: > 0 } atStake)
        {
            return true;
        }

        window.Value.ShowFromTray();

        var modal = new ConfirmationModalViewModel(
            title,
            "Stopped or lost:\n" + string.Join("\n", atStake.Select(x => $"  {x}")),
            IconKind.Warning,
            confirmText,
            "Keep working");

        await modalService.Value.Show(modal);

        return modal.Result;
    }

    public async Task<bool> StopAsync()
    {
        modalService.Value.DismissAll();

        bool stopped;

        using (backgroundTasks.Begin("Stopping work"))
        {
            stopped = await session.StopAllAsync(CancellationToken.None);
        }

        // Whatever the work raised on its way out.
        modalService.Value.DismissAll();

        return stopped;
    }
}
