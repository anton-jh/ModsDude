using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Services;
using System.Windows;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Services;

/// <summary>
/// Asks the server now and then whether the repo list, or the open repo's profile list, has changed
/// since it was read - and only asks. The answer lands on the services as pending changes, the
/// sidebar puts a dot on its refresh button, and the lists change when somebody presses it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only while somebody can see the answer.</b> Hidden to the tray or minimised, nothing is drawing
/// the dot, so a tick then is a request for nobody. Coming back is the moment the answer matters, so
/// that asks straight away rather than waiting out the interval.
/// </para>
/// <para>
/// <b>Failures are logged and nothing else.</b> Nobody asked for this check, so a network blip is
/// not worth an error dialog; the next tick asks again, and the refresh button still says what it
/// always said.
/// </para>
/// <para>
/// <b>The open repo's profiles only.</b> <see cref="ProfileService.Profiles"/> holds one repo at a
/// time, and a dot on a list nobody has open would have nowhere to be drawn.
/// </para>
/// </remarks>
public sealed class RemoteChangeWatcher(
    RepoRepository repoRepository,
    ProfileService profileService,
    ILogger<RemoteChangeWatcher> logger)
    : IDisposable
{
    /// <summary>Two small list reads a time, so this can be frequent without costing anything.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Restoring the window more often than this asks nothing new. Alt-tabbing through a minimised
    /// window is a lot of restores, and the answer from a moment ago is still the answer.
    /// </summary>
    private static readonly TimeSpan _minimumGap = TimeSpan.FromMinutes(1);

    private Window? _window;
    private DispatcherTimer? _timer;
    private DateTime _lastCheck = DateTime.MinValue;
    private bool _checking;


    public void Start(Window window)
    {
        _window = window;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
        };

        _timer.Tick += OnTick;
        _timer.Start();

        window.IsVisibleChanged += OnWindowVisibilityChanged;
        window.StateChanged += OnWindowStateChanged;
    }

    public void Dispose()
    {
        _timer?.Stop();

        if (_window is not null)
        {
            _window.IsVisibleChanged -= OnWindowVisibilityChanged;
            _window.StateChanged -= OnWindowStateChanged;
        }
    }


    private bool IsSeen => _window is { IsVisible: true } window && window.WindowState is not WindowState.Minimized;

    private void OnTick(object? sender, EventArgs e)
    {
        if (IsSeen)
        {
            _ = CheckAsync();
        }
    }

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        CheckIfDue();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        CheckIfDue();
    }

    private void CheckIfDue()
    {
        if (IsSeen && DateTime.UtcNow - _lastCheck >= _minimumGap)
        {
            _ = CheckAsync();
        }
    }

    private async Task CheckAsync()
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        _lastCheck = DateTime.UtcNow;

        try
        {
            // Separately, so that one list being unreadable does not keep the other one's dot away.
            try
            {
                await repoRepository.CheckForChanges(CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogInformation(exception, "Could not check the server for changes to the repo list.");
            }

            try
            {
                await profileService.CheckForChanges(CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogInformation(exception, "Could not check the server for changes to the profile list.");
            }
        }
        finally
        {
            _checking = false;
        }
    }
}
