using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Wpf.Shell.Navigation;
using System.Windows;

namespace ModsDude.Client.Wpf.Shell.Offline;

/// <summary>
/// Runs <see cref="ServerReconnect"/> for as long as the app does, and builds the open page again
/// once it reports the server back, so the page reads afresh what it could not while offline.
/// </summary>
public sealed class ReconnectWatcher(
    IServerReconnect reconnect,
    IShellNavigationService shellNavigation,
    ILogger<ReconnectWatcher> logger)
    : IReconnectWatcher
{
    private readonly CancellationTokenSource _stopping = new();


    public void Start()
    {
        reconnect.Reconnected += OnReconnected;
        // Observes its own failures; Dispose is what ends it.
        _ = RunAsync();
    }

    public void Dispose()
    {
        reconnect.Reconnected -= OnReconnected;
        _stopping.Cancel();
        _stopping.Dispose();
    }


    /// <summary>Owns the loop: it ends only by being stopped, and anything else is logged rather than lost.</summary>
    private async Task RunAsync()
    {
        try
        {
            await reconnect.RunAsync(_stopping.Token);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The reconnect loop stopped; the app will not notice the server coming back until it restarts.");
        }
    }

    private void OnReconnected(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(shellNavigation.ReloadOpenPage);
    }
}
