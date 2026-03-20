using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Extensions;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Models;
using ModsDude.Client.Wpf.Services;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;
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


    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var exception = e.Exception switch
        {
            UserFriendlyException userFriendlyException => userFriendlyException,
            Exception unknownException => UserFriendlyException.WrapUnknown(unknownException)
        };

        MessageBox.Show($"{exception.Message}\n\n{exception.DeveloperMessage}", "Oops", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }


    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<IModalService>(sp => sp.GetRequiredService<MainWindowViewModel>());

        services.AddFactory<MainPageViewModel>();
        services.AddFactory<CreateRepoPageViewModel>();
        services.AddSingleton<RepoAdminPageViewModelFactory>();
        services.AddSingleton<RepoPageViewModelFactory>();
        services.AddSingleton<CreateProfilePageViewModelFactory>();
        services.AddSingleton<ProfilePageViewModelFactory>();

        services.AddSingleton<NavigationLockService>();

        services.AddSingleton<RepoService>();
        services.AddSingleton<ProfileService>();

        services.AddCore<AuthenticationService>();
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<ClientConfiguration>();
        services.AddSingleton(new Store<State>("wpf.state.json"));
    }
}
