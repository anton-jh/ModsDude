using System.IO;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Builds;
using ModsDude.Client.Core.Updates;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace ModsDude.Client.Wpf.Shell.Updates;

/// <summary>Where the updater stands, for the Settings page.</summary>
public enum UpdateStage
{
    /// <summary>Not an installed copy - a debug build, or a portable one - so there is nothing to update.</summary>
    NotInstalled,

    UpToDate,
    Checking,
    Downloading,

    /// <summary>A newer version is on disk and installing it costs a restart.</summary>
    Ready,

    /// <summary>The last check or download failed. It is tried again on the next round.</summary>
    Failed
}


/// <summary>
/// Keeps an installed copy of the app up to date.
/// </summary>
/// <remarks>
/// <para>
/// <b>Downloads on its own and never restarts on its own.</b> The app spends most of its life in the tray,
/// possibly in the middle of an apply, and the one thing an updater must not do to somebody who is playing
/// is take the app away. So a new version is fetched quietly and then <em>announced</em> - in the column, the
/// tray menu and Settings - and installed when the user says so, or at the next start, whichever is first.
/// </para>
/// <para>
/// <b>Only an installed copy updates.</b> A debug build, or one run from a build folder, has no install to
/// replace; <see cref="IsInstalled"/> is false and every method is a quiet no-op, which is what keeps F5 from
/// ever touching the network for this.
/// </para>
/// <para>
/// <b>A failure is a state, not an error.</b> No network, a feed that is briefly unavailable and a release that
/// is still being uploaded are all ordinary. It is logged, shown as <see cref="UpdateStage.Failed"/> in
/// Settings, and tried again on the next round.
/// </para>
/// <para>
/// <b>Urgent once the server has moved past this build.</b> Nothing works until the update is installed, so
/// it is looked for straight away and then every minute, rather than every few hours.
/// </para>
/// </remarks>
/// <param name="githubRepository">The public repo whose releases are the feed. Null or empty means no feed.</param>
/// <param name="feedDirectory">
/// A folder to read releases from instead, for trying an update without publishing one. Wins over the repo.
/// </param>
public sealed class AppUpdater : IAppUpdater
{
    /// <summary>Long enough that a start at sign-in has finished being busy before it goes to the network.</summary>
    private static readonly TimeSpan _firstCheck = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan _interval = TimeSpan.FromHours(4);

    private static readonly TimeSpan _behindInterval = TimeSpan.FromMinutes(1);

    private readonly UpdateManager? _manager;
    private readonly IServerCompatibility _compatibility;
    private readonly Lazy<MainWindow> _window;
    private readonly ILogger<AppUpdater> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Timer? _timer;
    private VelopackAsset? _ready;
    private UpdateStage _stage;


    public AppUpdater(
        string? githubRepository,
        string? feedDirectory,
        IServerCompatibility compatibility,
        Lazy<MainWindow> window,
        ILogger<AppUpdater> logger)
    {
        _compatibility = compatibility;
        _window = window;
        _logger = logger;

        IUpdateSource? source = feedDirectory is { Length: > 0 }
            ? new SimpleFileSource(new DirectoryInfo(feedDirectory))
            : githubRepository is { Length: > 0 }
                ? new GithubSource(githubRepository, accessToken: null, prerelease: false)
                : null;

        if (source is not null)
        {
            _manager = new UpdateManager(source);
        }

        if (_manager?.IsInstalled is true)
        {
            // Downloaded on an earlier run and not applied: still waiting, and still worth saying.
            _ready = _manager.UpdatePendingRestart;
            _stage = _ready is null ? UpdateStage.UpToDate : UpdateStage.Ready;
        }
        else
        {
            _stage = UpdateStage.NotInstalled;
        }
    }


    public event EventHandler? Changed;


    public bool IsInstalled => _manager?.IsInstalled is true;

    public UpdateStage Stage => _stage;

    /// <summary>A package's version is 0.0.N for build N; see Directory.Build.props.</summary>
    public BuildNumber? ReadyBuild => _ready is null ? null : new BuildNumber(_ready.Version.Patch);

    public string StatusText => _stage switch
    {
        UpdateStage.NotInstalled => "Updates only apply to the installed copy of ModsDude.",
        UpdateStage.Checking => "Checking for updates...",
        UpdateStage.Downloading => "Downloading an update...",
        UpdateStage.Ready => $"{ReadyBuild} is ready. Restart to install it.",
        UpdateStage.Failed => "The last check for updates did not work. It is tried again later.",
        _ => "ModsDude is up to date."
    };

    public bool CanCheck => IsInstalled && _stage is not (UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Ready);


    public void Start()
    {
        if (IsInstalled is false)
        {
            return;
        }

        _timer = new Timer(_ => _ = CheckAsync(), null, _firstCheck, _interval);

        _compatibility.Changed += OnCompatibilityChanged;
    }

    public async Task CheckAsync()
    {
        if (_manager is null || IsInstalled is false || _stage is UpdateStage.Ready)
        {
            return;
        }

        if (await _gate.WaitAsync(0) is false)
        {
            return;
        }

        try
        {
            SetStage(UpdateStage.Checking);

            var found = await _manager.CheckForUpdatesAsync();

            if (found is null)
            {
                SetStage(UpdateStage.UpToDate);

                return;
            }

            SetStage(UpdateStage.Downloading);

            await _manager.DownloadUpdatesAsync(found);

            _ready = found.TargetFullRelease;

            SetStage(UpdateStage.Ready);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not check for or download an update.");

            SetStage(UpdateStage.Failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool ApplyPendingAtStartup(string[] arguments)
    {
        if (_manager is null || IsInstalled is false || _ready is null)
        {
            return false;
        }

        _logger.LogInformation("Installing the downloaded update {Build} at startup.", ReadyBuild);

        _manager.ApplyUpdatesAndRestart(_ready, arguments);

        return true;
    }

    public async Task RestartAsync()
    {
        if (_manager is null || _ready is null)
        {
            return;
        }

        // The same question closing the window asks, because it is the same thing: leaving with work
        // still running stops it part way.
        if (await _window.Value.PrepareForRestartAsync() is false)
        {
            return;
        }

        // Applied after this process has exited, by the updater, and then the app is started again. A
        // normal shutdown rather than an abrupt exit, so the tray icon and toasts are put away properly.
        _manager.WaitExitThenApplyUpdates(_ready, silent: false, restart: true);

        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _compatibility.Changed -= OnCompatibilityChanged;
        _timer?.Dispose();
        _gate.Dispose();
    }


    /// <summary>Never back to the slow pace: a server that has moved past this build stays past it.</summary>
    private void OnCompatibilityChanged(object? sender, EventArgs e)
    {
        if (_compatibility.Mismatch is { ClientIsBehind: true })
        {
            _timer?.Change(TimeSpan.Zero, _behindInterval);
        }
    }


    private void SetStage(UpdateStage stage)
    {
        _stage = stage;

        _logger.LogInformation("Update: {Stage}{Build}.", stage, stage is UpdateStage.Ready ? $" ({ReadyBuild})" : "");

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
