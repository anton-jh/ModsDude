using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Shared;

/// <summary>
/// Reads the repo list, and the profiles and savegames of every repo the client holds, again now and
/// then, so what other people changed shows up without anybody asking for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only while somebody can see the answer.</b> Hidden to the tray or minimised, nothing is drawing
/// it, so a tick then is a request for nobody. Coming back is the moment the answer matters, so that
/// reads straight away rather than waiting out the interval.
/// </para>
/// <para>
/// <b>Failures are logged and nothing else.</b> Nobody asked for this read, so a network blip is not
/// worth an error modal; the next tick reads again.
/// </para>
/// <para>
/// <b>The profiles of the repos a game here follows are read too</b>, held or not, because the drift
/// check compares those games against their profile's newest revision.
/// </para>
/// </remarks>
public sealed class RemoteChangeWatcher(
    IRepoStore repoStore,
    IProfileStore profileStore,
    ISavegameStore savegameStore,
    IGameRepository games,
    ILogger<RemoteChangeWatcher> logger)
    : IRemoteChangeWatcher
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Restoring the window more often than this reads nothing new. Alt-tabbing through a minimised
    /// window is a lot of restores, and the answer from a moment ago is still the answer.
    /// </summary>
    private static readonly TimeSpan _minimumGap = TimeSpan.FromMinutes(1);

    private Window? _window;
    private DispatcherTimer? _timer;
    private DateTime _lastRead = DateTime.MinValue;
    private bool _reading;


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

        // The first list arriving is the first moment there is anything to read profiles for.
        repoStore.Repos.CollectionChanged += OnReposChanged;
    }

    public void Dispose()
    {
        _timer?.Stop();
        repoStore.Repos.CollectionChanged -= OnReposChanged;

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
            _ = ReadAsync();
        }
    }

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ReadIfDue();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        ReadIfDue();
    }

    private void OnReposChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ReadIfDue();
    }

    private void ReadIfDue()
    {
        if (IsSeen && DateTime.UtcNow - _lastRead >= _minimumGap)
        {
            _ = ReadAsync();
        }
    }

    /// <summary>Observes its own failures, so the callers above have nothing to await.</summary>
    private async Task ReadAsync()
    {
        // Before sign-in, and until the shell's first load is in, there is nothing to read again:
        // that load owns the list until then.
        if (_reading || repoStore.HasLoaded is false)
        {
            return;
        }

        _reading = true;
        _lastRead = DateTime.UtcNow;

        try
        {
            try
            {
                await repoStore.RefreshRepos(CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogInformation(exception, "Could not read the repo list again.");
            }

            // Separately, so that one repo being unreadable does not keep the others' changes away.
            foreach (var repoId in ProfileReposToRead())
            {
                try
                {
                    await profileStore.RefreshAsync(repoId, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogInformation(exception, "Could not read the profiles of repo {RepoId} again.", repoId);
                }
            }

            foreach (var repoId in savegameStore.LoadedRepos.Where(x => repoStore.Repos.Any(repo => repo.Id == x)))
            {
                try
                {
                    await savegameStore.RefreshAsync(repoId, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogInformation(exception, "Could not read the savegames of repo {RepoId} again.", repoId);
                }
            }
        }
        finally
        {
            _reading = false;
        }
    }

    private IReadOnlyList<Guid> ProfileReposToRead()
        => [.. profileStore.LoadedRepos
            .Concat(games.Games.Select(x => x.ActiveProfile?.RepoId).OfType<Guid>())
            .Where(x => repoStore.Repos.Any(repo => repo.Id == x))
            .Distinct()
            .Order()];
}
