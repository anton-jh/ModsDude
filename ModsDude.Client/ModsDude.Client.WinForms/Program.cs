using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Extensions;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.WinForms.Authentication;
using ModsDude.Client.WinForms.Models.Persistence;

namespace ModsDude.Client.WinForms;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static async Task Main()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
            })
            .ConfigureServices(ConfigureServices)
            .Build();

        await host.StartAsync();


        Application.SetColorMode(SystemColorMode.Dark);
        ApplicationConfiguration.Initialize();

        var mainWindow = host.Services.GetRequiredService<MainWindow>();


        Application.Run(mainWindow);

        await host.StopAsync();
    }


    internal static void ConfigureServices(HostBuilderContext ctx, IServiceCollection services)
    {
        services.AddCore<AuthenticationService>();
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<ClientConfiguration>();
        services.AddSingleton(new Store<State>("cli.state.json"));
        services.AddTransient<MainWindow>();
    }
}
