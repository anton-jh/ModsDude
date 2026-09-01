using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Extensions;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Diagnostics;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.Services;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        _configuration = builder.Build();

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, _configuration);

        _serviceProvider = serviceCollection.BuildServiceProvider();

        // The one thing the container cannot reach: an attached behaviour XAML constructs itself.
        LazyLoad.UseDiagnostics(
            _serviceProvider.GetRequiredService<ILogger<App>>(),
            _serviceProvider.GetRequiredService<IBackgroundProblemReporter>());

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        window.Show();

        await _serviceProvider.GetRequiredService<AuthenticationService>().Get(default);
    }


    private async void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        // The dialog tells the user what went wrong; the log is what tells anyone why. The wrapped
        // exception loses the original stack, so this logs what actually arrived.
        _serviceProvider.GetRequiredService<ILogger<App>>()
            .LogError(e.Exception, "Unhandled exception reached the dispatcher.");

        var exception = e.Exception switch
        {
            UserFriendlyException userFriendlyException => userFriendlyException,
            Exception unknownException => UserFriendlyException.WrapUnknown(unknownException)
        };

        var modalService = _serviceProvider.GetRequiredService<IModalService>();
        var modal = ConfirmationDialogViewModel.Error(exception);
        await modalService.Show(modal);
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
        services.AddSingleton<CreateLocalInstancePageViewModel.Factory>();
        services.AddSingleton<EditLocalInstancePageViewModel.Factory>();
        services.AddSingleton<InstancePageViewModel.Factory>();
        services.AddSingleton<SyncPageViewModel.Factory>();
        services.AddSingleton<RepoModsPageViewModel.Factory>();

        services.AddSingleton<NavigationLockService>();
        services.AddTransient<NavigationManager>();

        // One notice for the whole app, and one way in to it from outside the sidebar.
        services.AddSingleton<ShellNavigationService>();
        services.AddSingleton<ProfileApplyService>();
        services.AddSingleton<DriftNotificationViewModel>();

        // Both faces of one object again: everything that absorbs a failure reports it through the
        // interface, and the shell draws the notice those reports add up to.
        services.AddSingleton<BackgroundProblemViewModel>();
        services.AddSingleton<IBackgroundProblemReporter>(sp => sp.GetRequiredService<BackgroundProblemViewModel>());

        services.AddSingleton<ModListItemViewModel.Factory>();

        // Singleton because switching user is what replaces the shell it is drawn in.
        services.AddSingleton<AccountViewModel>();

        services.AddSingleton<IModalService>(sp => sp.GetRequiredService<MainWindowViewModel>());
        // The shell is the modal host, so anything the shell itself is built from has to ask for the
        // host after the fact rather than as a constructor argument. See ProfileApplyService.
        services.AddSingleton(sp => new Lazy<IModalService>(sp.GetRequiredService<IModalService>));
        services.AddSingleton<IDialogService, DialogService>();

        // Singleton so the decoded thumbnails survive navigating away from a page and back.
        services.AddSingleton<IModImageProvider, ModImageProvider>();

        // One cache per machine, not one per volume: images are always copies, so the hardlink
        // constraint that makes content stores per-volume does not apply to them.
        services.AddSingleton(sp => new ModImageCache(() => sp.GetRequiredService<ClientSettingsRepository>().Settings.ImageCache));
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
        services.AddSingleton<LocalInstanceRepository>();

        // Sync's store eviction has to spare what other instances are running, and the instance list
        // is the only thing that knows which folders those are.
        services.AddSingleton<IInstanceModFolders>(sp => sp.GetRequiredService<LocalInstanceRepository>());

        // The drift monitor asks the same list for the folder and the standing intent behind each one.
        services.AddSingleton<IDriftCandidateSource>(sp => sp.GetRequiredService<LocalInstanceRepository>());
        services.AddSingleton<ClientSettingsRepository>();
        // A catalog is created per surface and disposed with it, so its per-source scan cache lives
        // exactly as long as the page whose checkboxes recompose from it.
        services.AddSingleton<ModCatalog.Factory>();
        services.AddSingleton<LastSelectionRepository>();

        // What the shell drops when the signed-in user changes. Everything else the client holds
        // describes this machine's game installations and survives the switch - see IUserScopedState.
        services.AddSingleton<IUserScopedState>(sp => sp.GetRequiredService<RepoRepository>());
        services.AddSingleton<IUserScopedState>(sp => sp.GetRequiredService<ProfileService>());

        services.AddCore<AuthenticationService>(configuration["ModsDudeServer:BaseUrl"]
            ?? throw new InvalidOperationException("'ModsDudeServer:BaseUrl' is missing from appsettings.json."));
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<ClientConfiguration>();
        services.AddSingleton<StateStore>();
    }
}
