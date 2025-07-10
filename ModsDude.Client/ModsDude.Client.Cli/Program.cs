using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModsDude.Client.Cli.Authentication;
using ModsDude.Client.Cli.Commands.Misc;
using ModsDude.Client.Cli.Commands.Repos;
using ModsDude.Client.Cli.DependencyInjection;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.ModsDudeServer;
using Spectre.Console;
using Spectre.Console.Cli;


var builder = Host.CreateDefaultBuilder(args);


builder.ConfigureServices(static (ctx, services) =>
{
    services.AddSingleton(AnsiConsole.Console);
    services.AddSingleton<AuthenticationService>();
    services.AddSingleton<IAccessTokenAccessor>(sp => sp.GetRequiredService<AuthenticationService>());
    services.AddSingleton<ClientConfiguration>();
    services.AddModsDudeClient();
});


var registrar = new TypeRegistrar(builder);
var app = new CommandApp<MenuCommand>(registrar);


app.Configure(config =>
{
    //config.PropagateExceptions();

    config.AddCommand<ReloginCommand>("re-login").WithAlias("logout");
    config.AddCommand<ListReposCommand>("list-repos").WithAlias("repos");
    config.AddCommand<CreateRepoCommand>("create-repo");
});


await app.RunAsync(args);
