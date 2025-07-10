using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModsDude.Cli.Commands;
using ModsDude.Cli.DependencyInjection;
using ModsDude.Client.Cli.Authentication;
using ModsDude.Client.Cli.Commands;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.ModsDudeServer;
using Spectre.Console;
using Spectre.Console.Cli;


var builder = Host.CreateDefaultBuilder(args);


builder.ConfigureServices(static (ctx, services) =>
{
    services.AddSingleton(AnsiConsole.Console);
    services.AddSingleton<IAccessTokenAccessor, AuthenticationService>();
    services.AddSingleton<ClientConfiguration>();
    services.AddModsDudeClient();
});


var registrar = new TypeRegistrar(builder);
var app = new CommandApp<ListReposCommand>(registrar);


app.Configure(config =>
{
    config.AddCommand<GreetingCommand>("greet");
    config.AddCommand<ListReposCommand>("list-repos").WithAlias("repos").WithAlias("my-repos");
});


await app.RunAsync(args);
