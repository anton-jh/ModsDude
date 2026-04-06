using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Extensions;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.Services;
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
        ConfigureServices(serviceCollection);

        _serviceProvider = serviceCollection.BuildServiceProvider();

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        window.Show();

        await _serviceProvider.GetRequiredService<AuthenticationService>().Get(default);
    }


    private async void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        var exception = e.Exception switch
        {
            UserFriendlyException userFriendlyException => userFriendlyException,
            Exception unknownException => UserFriendlyException.WrapUnknown(unknownException)
        };

        var modalService = _serviceProvider.GetRequiredService<IModalService>();
        var modal = ConfirmationDialogViewModel.Error(exception);
        await modalService.Show(modal);
    }


    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddFactory<MainPageViewModel>();
        services.AddFactory<CreateRepoPageViewModel>();
        services.AddSingleton<RepoAdminPageViewModel.Factory>();
        services.AddSingleton<RepoPageViewModel.Factory>();
        services.AddSingleton<CreateProfilePageViewModel.Factory>();
        services.AddSingleton<ProfilePageViewModel.Factory>();
        services.AddSingleton<EditProfilePageViewModel.Factory>();
        services.AddSingleton<ProfileModsEditorPageViewModel.Factory>();
        services.AddSingleton<CreateLocalInstancePageViewModel.Factory>();
        services.AddSingleton<EditLocalInstancePageViewModel.Factory>();

        services.AddSingleton<NavigationLockService>();
        services.AddTransient<NavigationManager>();

        services.AddSingleton<IModalService>(sp => sp.GetRequiredService<MainWindowViewModel>());
        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton<RepoService>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<LocalInstanceService>();

        services.AddCore<AuthenticationService>();
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<ClientConfiguration>();
        services.AddSingleton<StateStore>();
    }
}
