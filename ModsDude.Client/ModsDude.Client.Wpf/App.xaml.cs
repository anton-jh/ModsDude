using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Extensions;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.Notices;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Startup;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Updates;
using ModsDude.Client.Wpf.Updates;
using ModsDude.Client.Wpf.Diagnostics;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.Services;
using ModsDude.Client.Wpf.Tray;
using ModsDude.Client.Wpf.View.Behaviors;
using ModsDude.Client.Wpf.View.Imaging;
using ModsDude.Client.Wpf.View.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using ModsDude.Client.Wpf.ViewModel.Windows;
using ModsDude.Shared.GenericFactories;
using System.IO;
using System.Windows;

namespace ModsDude.Client.Wpf;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IServiceProvider _serviceProvider = null!;
    private IConfiguration _configuration = null!;

    private SingleInstance? _singleInstance;
    private TrayService? _tray;
    private DriftBackstop? _backstop;
    private RemoteChangeWatcher? _remoteChanges;
    private SavegameClaimWatcher? _claimWatcher;
    private FriendActivityWatcher? _friendWatcher;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Before anything that has a path, a name or a setting: which install this is decides where
        // its state lives, what its mutex is called and which appsettings it reads.
        var environment = ResolveEnvironment();
        AppIdentity.Initialize(environment);

        // Started by Windows at sign-in rather than by the user: up in the tray, no window, and no
        // browser. See AutostartService.
        var background = e.Args.Contains(AutostartService.BackgroundArgument, StringComparer.OrdinalIgnoreCase);

        // Before configuration or the container: a second copy has nothing to build. It has told the
        // first one to come forward - unless it was a background start, which asks for nothing - and
        // has no reason to exist any longer.
        _singleInstance = SingleInstance.TryAcquire(bringExistingForward: background is false);

        if (_singleInstance is null)
        {
            Shutdown();

            return;
        }

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            // Layered on top, and absent until somebody writes one: the deployed server's address
            // lives in appsettings.Production.json, the local one stays in the base file.
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);

        _configuration = builder.Build();

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, _configuration);

        _serviceProvider = serviceCollection.BuildServiceProvider();

        // First instance, and nothing shown yet: the one safe moment to install what was downloaded on an
        // earlier run. See AppUpdater.ApplyPendingAtStartup.
        if (_serviceProvider.GetRequiredService<AppUpdater>().ApplyPendingAtStartup(e.Args))
        {
            Shutdown();

            return;
        }

        // Two ways out of the process that the dispatcher handler never sees: a throw on a thread
        // that is not the UI one, and a Task nobody awaited. Both used to be silent.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // The one thing the container cannot reach: an attached behaviour XAML constructs itself.
        LazyLoad.UseDiagnostics(
            _serviceProvider.GetRequiredService<ILogger<App>>(),
            _serviceProvider.GetRequiredService<IBackgroundProblemReporter>());

        // Before the first window, so the taskbar button is filed under the same identity as the
        // notifications and the shortcut that makes Windows show them.
        _serviceProvider.GetRequiredService<WindowsToasts>().Register(
            AppIdentity.Name,
            AppIdentity.DisplayName,
            Environment.ProcessPath,
            // An installed copy has the installer's shortcut, made with this same identity.
            ensureShortcut: _serviceProvider.GetRequiredService<AppUpdater>().IsInstalled is false);

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>();

        if (background is false)
        {
            window.Show();
        }

        var trayUp = StartBackgroundPresence(window);

        if (background && trayUp is false)
        {
            // Hidden with no icon to bring it back is an app nobody can reach, so it comes up as an
            // ordinary start instead.
            window.Show();
            background = false;
        }

        // The opposite of the background presence above: it only asks while the window is in sight,
        // because what it finds is a dot in the sidebar and nothing else.
        _remoteChanges = _serviceProvider.GetRequiredService<RemoteChangeWatcher>();
        _remoteChanges.Start(window);

        RepairAutostart();

        TidyStoresInBackground();

        var authentication = _serviceProvider.GetRequiredService<AuthenticationService>();
        var connection = _serviceProvider.GetRequiredService<ConnectionRetry>();

        if (background)
        {
            await SignInWithoutInterruptingAsync(authentication, connection, window);

            return;
        }

        await connection.RunAsync(ConnectionTarget.SignIn, authentication.Get, CancellationToken.None);
    }


    /// <summary>
    /// Signs in for a start nobody is watching: quietly if the cached account allows it, and otherwise
    /// only once the window has been opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Silent is worth having, not merely permitted.</b> The drift check runs off persisted state and
    /// needs no account, but what a notice can say about a repo - and everything a repo-scoped toast
    /// will offer - needs the shell built for somebody, and that is what signing in does.
    /// </para>
    /// <para>
    /// <b>A failure is not a reason to open a browser.</b> Logon is exactly when the network is not up
    /// yet, which arrives here as an exception rather than as "needs the user" - so it is logged, and the
    /// full sign-in waits for the window like the interactive case does. Nothing is retried on a timer
    /// while the window is closed: the next thing that needs the account, opening the window, is the
    /// retry - and from there it is retried like any other start.
    /// </para>
    /// </remarks>
    private async Task SignInWithoutInterruptingAsync(AuthenticationService authentication, ConnectionRetry connection, MainWindow window)
    {
        try
        {
            if (await authentication.TrySignInSilentlyAsync(default))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            _serviceProvider.GetRequiredService<ILogger<App>>()
                .LogWarning(exception, "Could not sign in quietly at startup; waiting for the window to be opened.");
        }

        await window.WaitUntilShownAsync();

        await connection.RunAsync(ConnectionTarget.SignIn, authentication.Get, CancellationToken.None);
    }

    /// <summary>
    /// Keeps a start-at-sign-in entry pointing at where the app actually is, for the install that has one.
    /// </summary>
    /// <remarks>
    /// Logged and forgotten: a registry that cannot be written is a startup entry that stays as it was,
    /// and that is no reason for the app not to come up.
    /// </remarks>
    private void RepairAutostart()
    {
        try
        {
            _serviceProvider.GetRequiredService<AutostartService>().Reconcile();
        }
        catch (Exception exception)
        {
            _serviceProvider.GetRequiredService<ILogger<App>>()
                .LogWarning(exception, "Could not check the start-with-Windows entry.");
        }
    }


    /// <summary>
    /// Which environment this process is, and from that which install it is. See <see cref="AppIdentity"/>.
    /// </summary>
    /// <remarks>
    /// <b>The build configuration decides, unless told otherwise.</b> A WPF app has no launch profile
    /// setting <c>DOTNET_ENVIRONMENT</c> for it - Visual Studio only sets one where a profile asks -
    /// so leaving it to the variable would make every run Production, including the debug build that
    /// must never touch the real install's files. The variable still wins where it is set, which is
    /// the conventional way to run a build as something else.
    /// </remarks>
    private static string ResolveEnvironment()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") is { Length: > 0 } fromHost)
        {
            return fromHost;
        }

#if DEBUG
        return "Development";
#else
        return AppIdentity.ProductionEnvironment;
#endif
    }


    /// <summary>
    /// Everything that lets the app go on being useful with its window out of sight: the tray icon,
    /// the checks that do not wait to be activated, and the way a second launch reaches this copy.
    /// </summary>
    /// <returns>Whether the tray icon is up, which is what decides whether hiding the window is safe.</returns>
    private bool StartBackgroundPresence(MainWindow window)
    {
        _singleInstance!.ListenForActivation(() => Dispatcher.InvokeAsync(window.ShowFromTray));

        // Windows ending the session must not be answered with a window that will not close.
        SessionEnding += (_, _) => window.AllowClose();

        _serviceProvider.GetRequiredService<ToastNotifier>().Start();

        _tray = _serviceProvider.GetRequiredService<TrayService>();
        var trayUp = _tray.Start();

        _backstop = _serviceProvider.GetRequiredService<DriftBackstop>();
        _backstop.Start();

        _claimWatcher = _serviceProvider.GetRequiredService<SavegameClaimWatcher>();
        _claimWatcher.Start();

        _friendWatcher = _serviceProvider.GetRequiredService<FriendActivityWatcher>();
        _friendWatcher.Start();

        _serviceProvider.GetRequiredService<AppUpdater>().Start();

        return trayUp;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Nobody is left to answer a click on what is still in Action Center, so it goes with the app.
        _serviceProvider?.GetService<WindowsToasts>()?.ClearAll();

        _backstop?.Dispose();
        _remoteChanges?.Dispose();
        _claimWatcher?.Dispose();
        _friendWatcher?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }


    /// <summary>
    /// Puts every content store back inside its size limit, once, on the way up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The safety net under the limit.</b> Every apply sweeps the store it used, which covers the
    /// ordinary case - but a store that no longer serves any mod folder is never visited by a sync at
    /// all, and that is precisely the store quietly holding tens of gigabytes of a game somebody
    /// uninstalled. Startup is the one moment guaranteed to come round for it.
    /// </para>
    /// <para>
    /// <b>Off the UI thread and never awaited.</b> It walks each store's blob tree, which is thousands
    /// of files, and nothing about showing a window depends on the answer. It logs; it has no other
    /// way to fail, because a store that could not be tidied is only a store that is still too big.
    /// </para>
    /// </remarks>
    private void TidyStoresInBackground()
    {
        var maintenance = _serviceProvider.GetRequiredService<ContentStoreMaintenance>();
        var log = _serviceProvider.GetRequiredService<ILogger<App>>();

        _ = Task.Run(async () =>
        {
            try
            {
                await maintenance.SweepAllAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                log.LogWarning(exception, "Could not tidy the content stores at startup.");
            }
        });
    }


    private async void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        await _serviceProvider.GetRequiredService<IErrorReporter>()
            .ShowAsync(e.Exception, "handling something on the UI thread");
    }

    /// <summary>
    /// A failed <see cref="Task"/> nobody awaited. It reaches here when the task is collected, which
    /// is long after the fact and on a finalizer thread - so there is nothing to show and nothing to
    /// interrupt, only something to write down.
    /// </summary>
    /// <remarks>
    /// Marked observed, deliberately. Leaving it unobserved is a process kill on an app that is very
    /// likely still working, and a fire-and-forget continuation that failed has already cost
    /// whatever it was going to cost by the time this runs.
    /// </remarks>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _serviceProvider.GetRequiredService<ILogger<App>>()
            .LogError(e.Exception, "A task failed and nothing was awaiting it.");

        e.SetObserved();
    }

    /// <summary>
    /// The last thing that runs. Nothing here can stop the process - the runtime is on its way down
    /// - so the only useful act is getting the exception into the file before it goes.
    /// </summary>
    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        _serviceProvider.GetRequiredService<ILogger<App>>()
            .LogCritical(e.ExceptionObject as Exception, "Unhandled exception; terminating: {Terminating}.", e.IsTerminating);
    }


    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // A WPF app has no console, so an ILogger with no file behind it is the same as no
        // logger at all. Registered first, so everything composed below can ask for one.
        services.AddLogging(builder =>
        {
            // Configurable so that chasing a quiet failure - imagery that publishes nothing, and
            // says so only at Debug - is an appsettings edit rather than a rebuild.
            builder.SetMinimumLevel(Enum.TryParse<LogLevel>(configuration["Logging:MinimumLevel"], out var level)
                ? level
                : LogLevel.Information);
            // The typed clients log a line per request at Information, which would bury
            // everything worth reading.
            builder.AddFilter("System.Net.Http", LogLevel.Warning);
            builder.AddProvider(new FileLoggerProvider());
        });

        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        // Who is left running when the window is closed: the icon that brings it back, and the checks
        // that no longer wait for it to be looked at.
        services.AddSingleton<TrayService>();
        services.AddSingleton<DriftBackstop>();

        // Asks whether the sidebar's lists are behind the server, and only says so - see the class.
        services.AddSingleton<RemoteChangeWatcher>();

        // Asks who holds the savegames checked out here, so a takeover reaches the notice - see the class.
        services.AddSingleton<SavegameClaimWatcher>();

        // Reads what friends are on, so a switch or a check-out reaches the column - see the class.
        services.AddSingleton<FriendActivityWatcher>();

        // Keeps trying sign-in and the first repo load until something answers. Told about the sign-in
        // library's own way of saying so, which Core cannot see.
        services.AddSingleton(sp => new ConnectionRetry(
            sp.GetRequiredService<ILogger<ConnectionRetry>>(),
            alsoConnectionFailure: AuthenticationService.IsUnreachable));

        // Windows notifications: the toolkit behind one seam, and the object that decides when the
        // window's own notices and toasts are worth sending through it.
        services.AddSingleton<WindowsToasts>();
        services.AddSingleton<ISystemToasts>(sp => sp.GetRequiredService<WindowsToasts>());
        services.AddSingleton<ToastNotifier>();

        // Updates for an installed copy. One object seen two ways: the column and the tray ask it whether
        // something is waiting, the Settings page and the shell ask it for the rest.
        services.AddSingleton(sp => new Lazy<MainWindow>(sp.GetRequiredService<MainWindow>));
        services.AddSingleton(sp => new AppUpdater(
            configuration["Updates:GithubRepository"],
            configuration["Updates:Directory"],
            sp.GetRequiredService<Lazy<MainWindow>>(),
            sp.GetRequiredService<ILogger<AppUpdater>>()));
        services.AddSingleton<IUpdateStatus>(sp => sp.GetRequiredService<AppUpdater>());

        // Only the production install ever registers itself; see AutostartService for why, and for
        // why a debug build running under the dotnet host has nothing stable to register anyway.
        services.AddSingleton<IStartupRegistry, RegistryStartupRegistry>();
        services.AddSingleton(sp => new AutostartService(
            sp.GetRequiredService<IStartupRegistry>(),
            AppIdentity.Name,
            Environment.ProcessPath,
            AppIdentity.IsProduction));

        services.AddFactory<MainPageViewModel>();
        services.AddFactory<CreateRepoPageViewModel>();
        services.AddFactory<SettingsPageViewModel>();
        services.AddSingleton<RepoAdminPageViewModel.Factory>();
        services.AddSingleton<RepoOverviewPageViewModel.Factory>();
        services.AddSingleton<RepoMembersPageViewModel.Factory>();
        services.AddSingleton<JoinRepoPageViewModel.Factory>();
        services.AddSingleton<RepoPageViewModel.Factory>();
        services.AddSingleton<CreateProfilePageViewModel.Factory>();
        services.AddSingleton<ProfilePageViewModel.Factory>();
        services.AddSingleton<ProfileOverviewPageViewModel.Factory>();
        services.AddSingleton<EditProfilePageViewModel.Factory>();
        services.AddSingleton<ProfileModsEditorPageViewModel.Factory>();
        services.AddSingleton<ProfileModsPageViewModel.Factory>();
        services.AddSingleton<ProfileHistoryPageViewModel.Factory>();
        services.AddSingleton<ConnectGamePageViewModel.Factory>();
        services.AddSingleton<GameSettingsPageViewModel.Factory>();
        services.AddSingleton<RepoModsPageViewModel.Factory>();
        services.AddSingleton<RepoSavegamesPageViewModel.Factory>();
        services.AddSingleton<RepoArchivePageViewModel.Factory>();
        services.AddFactory<ArchivePageViewModel>();
        services.AddFactory<HomePageViewModel>();
        services.AddSingleton<FriendActivityListViewModel.Factory>();

        services.AddSingleton<NavigationLockService>();
        services.AddTransient<NavigationManager>();

        // One table of who is touching what, for the whole process. A singleton is not a convenience
        // here - it is the entire mechanism, since two of these would be two sets of claims that
        // cannot see each other and therefore no claims at all.
        services.AddSingleton<IResourceLeases, ResourceLeases>();

        // One notice for the whole app, and one way in to it from outside the sidebar.
        services.AddSingleton<ShellNavigationService>();
        services.AddSingleton<ProfileApplyService>();

        // What friends are on: the report every activation makes, the one read every surface draws from,
        // and the one gesture that follows them. See FriendActivityService.
        services.AddSingleton<GameActivityReporter>();
        services.AddSingleton<IFriendActivitySeen, StateStoreFriendActivitySeen>();
        services.AddSingleton<FriendActivityService>();
        services.AddSingleton<IUserScopedState>(sp => sp.GetRequiredService<FriendActivityService>());
        services.AddSingleton<IFriendActivityEnvironment, FriendActivityEnvironment>();
        services.AddSingleton<FriendFollowService>();

        // Where the profile a game follows stands against its folders, asked once for the sidebar rows,
        // the repo entries and the header rather than three times with three chances to disagree.
        services.AddSingleton<ProfileSyncStatusService>();

        // The other half of that pair: one way into an import, so the repo claim and the two
        // questions an import cannot answer for itself live somewhere a new page cannot forget them.
        services.AddSingleton<ModImportCoordinator>();

        // And the third of the set: a profile save claims the profile, runs the import under it, and
        // outlives the editor that started it - which is what stopped a navigation mid-upload from
        // registering the files and never writing the revision.
        services.AddSingleton<ProfileSaveService>();

        // Check-in is reached from a slot row and from the check-out dialog's way out of a refused
        // slot, so the ask-send-resolve-a-stale-base sequence lives in one object rather than two.
        services.AddSingleton<SavegameFlowService>();

        // The column on the right and the two things it is built from: what the notices are allowed
        // to ask the running app, and what the user has waved away.
        services.AddSingleton<NoticeCenterViewModel>();
        services.AddSingleton<INoticeEnvironment, NoticeEnvironment>();
        services.AddSingleton<DismissalLedger>();

        // Both faces of one object again: everything that absorbs a failure reports it through the
        // interface, and the column draws a notice per kind out of what those reports add up to.
        services.AddSingleton<BackgroundProblemSource>();
        services.AddSingleton<IBackgroundProblemReporter>(sp => sp.GetRequiredService<BackgroundProblemSource>());

        // And once more for work in progress: everything long-running announces itself through the
        // interface, and the shell draws the strip along the top out of whatever is still running.
        services.AddSingleton<BackgroundTaskViewModel>();
        services.AddSingleton<IBackgroundTaskReporter>(sp => sp.GetRequiredService<BackgroundTaskViewModel>());

        // Both faces once more: everything that has just done something says so through the interface,
        // and the shell draws the card along the bottom edge out of what is still up.
        services.AddSingleton<ToastCenterViewModel>();
        services.AddSingleton<IToastService>(sp => sp.GetRequiredService<ToastCenterViewModel>());

        services.AddSingleton<ModListItemViewModel.Factory>();

        // Singleton because switching user is what replaces the shell it is drawn in.
        services.AddSingleton<AccountViewModel>();

        services.AddSingleton<IModalService>(sp => sp.GetRequiredService<MainWindowViewModel>());
        // The shell is the modal host, so anything the shell itself is built from has to ask for the
        // host after the fact rather than as a constructor argument. See ProfileApplyService.
        services.AddSingleton(sp => new Lazy<IModalService>(sp.GetRequiredService<IModalService>));
        services.AddSingleton<IDialogService, DialogService>();

        // Everything the user is told went wrong goes through here, which is what makes the log a
        // by-product of the dialog rather than a second thing every catch block has to remember.
        services.AddSingleton<IErrorReporter, ErrorReporter>();

        // Singleton so the decoded thumbnails survive navigating away from a page and back.
        services.AddSingleton<IModImageProvider, ModImageProvider>();

        // One cache per machine, not one per volume: images are always copies, so the hardlink
        // constraint that makes content stores per-volume does not apply to them.
        services.AddSingleton(sp => new ModImageCache(
            () => sp.GetRequiredService<ClientSettingsRepository>().Settings.ImageCache,
            sp.GetRequiredService<ILogger<ModImageCache>>()));
        services.AddSingleton<IModImageStore, ModImageStore>();
        services.AddSingleton<IModImagerySource, ModImagerySource>();

        // Both faces of one object: the import track fires it and forgets, and a row about to draw
        // a registered version with no imagery waits for what came back.
        services.AddSingleton<ModImagePublisher>();
        services.AddSingleton<IModImagePublisher>(sp => sp.GetRequiredService<ModImagePublisher>());
        services.AddSingleton<IModImageBackfill>(sp => sp.GetRequiredService<ModImagePublisher>());

        services.AddSingleton<RepoRepository>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<MembershipService>();
        services.AddSingleton<InviteService>();
        services.AddSingleton<CurrentUserService>();
        services.AddSingleton<GameRepository>();

        // Sync's store eviction has to spare what other games are running, and the game list
        // is the only thing that knows which folders those are.
        services.AddSingleton<IModFolders>(sp => sp.GetRequiredService<GameRepository>());

        // The drift monitor asks the same list for the folder and the standing intent behind each one.
        services.AddSingleton<IDriftCandidateSource>(sp => sp.GetRequiredService<GameRepository>());
        services.AddSingleton<ClientSettingsRepository>();
        // A catalog is created per surface and disposed with it, so its per-source scan cache lives
        // exactly as long as the page whose checkboxes recompose from it.
        services.AddSingleton<ModCatalog.Factory>();
        services.AddSingleton<LastSelectionRepository>();

        // What the shell drops when the signed-in user changes. Everything else the client holds
        // describes this machine's game installations and survives the switch - see IUserScopedState.
        services.AddSingleton<IUserScopedState>(sp => sp.GetRequiredService<RepoRepository>());
        services.AddSingleton<IUserScopedState>(sp => sp.GetRequiredService<ProfileService>());

        // The same object again, for the one fact the drift check needs of it: which revision each
        // profile it has loaded is on. It answers null for every other repo, which is why the check
        // still works before anything has been loaded at all.
        services.AddSingleton<IProfileRevisions>(sp => sp.GetRequiredService<ProfileService>());

        // The savegame counterpart, populated as a side effect of reading a savegame list - the Saves
        // page, or the claim watch reading the lists of whatever this machine holds. Registered under
        // both names for the same reason: those record into it, the drift check reads it, and they
        // have to be the one object.
        services.AddSingleton<SavegameSightingCache>();
        services.AddSingleton<ISavegameSightings>(sp => sp.GetRequiredService<SavegameSightingCache>());
        services.AddSingleton<SavegameClaimWatch>();

        services.AddCore<AuthenticationService>(configuration["ModsDudeServer:BaseUrl"]
            ?? throw new InvalidOperationException("'ModsDudeServer:BaseUrl' is missing from appsettings.json."));
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<ClientConfiguration>();
        services.AddSingleton<StateStore>();
    }
}
