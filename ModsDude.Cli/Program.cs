using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModsDude.Cli.Commands;
using ModsDude.Cli.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;


var builder = Host.CreateDefaultBuilder(args);


builder.ConfigureServices(static (ctx, services) =>
{
    services.AddSingleton(AnsiConsole.Console);
});


var registrar = new TypeRegistrar(builder);
var app = new CommandApp(registrar);


app.Configure(config =>
{
    config.AddCommand<GreetingCommand>("greet");
});


await app.RunAsync(args);
