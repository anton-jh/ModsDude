using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModsDude.Cli.DependencyInjection;
using Spectre.Console;


await Host.CreateDefaultBuilder(args)
    .ConfigureServices(static (ctx, services) =>
    {
        services.AddSingleton(AnsiConsole.Console);
    })
    .BuildApp()
    .RunAsync(args);
