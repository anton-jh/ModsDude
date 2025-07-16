using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Cli.Authentication;
using ModsDude.Client.Cli.Commands.Misc;
using ModsDude.Client.Cli.Commands.Profiles;
using ModsDude.Client.Cli.Commands.Repos;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.DependencyInjection;
using ModsDude.Client.Cli.DynamicForms;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.ModsDudeServer;
using Spectre.Console;
using Spectre.Console.Cli;


var builder = Host.CreateDefaultBuilder(args);


builder.ConfigureLogging(logging =>
{
    logging.ClearProviders();
});

builder.ConfigureServices(static (ctx, services) =>
{
    services.AddSingleton(AnsiConsole.Console);
    services.AddSingleton<AuthenticationService>();
    services.AddSingleton<IAccessTokenAccessor>(sp => sp.GetRequiredService<AuthenticationService>());
    services.AddSingleton<ClientConfiguration>();
    services.AddSingleton<FormPrompter>();
    services.AddSingleton<RepoCollector>();
    services.AddModsDudeClient();
    services.AddGameAdapters(typeof(IGameAdapter).Assembly);
});


var registrar = new TypeRegistrar(builder);
var app = new CommandApp<MenuCommand>(registrar);


app.Configure(config =>
{
    //config.PropagateExceptions();

    config.AddCommand<ReloginCommand>("re-login").WithAlias("logout");
    config.AddCommand<ListReposCommand>("list-repos").WithAlias("repos");
    config.AddCommand<CreateRepoCommand>("create-repo");
    config.AddCommand<DeleteRepoCommand>("delete-repo");
    config.AddCommand<EditRepoCommand>("edit-repo");
    config.AddCommand<RepoDetailsCommand>("inspect-repo");
    config.AddCommand<ListProfilesCommand>("list-profiles").WithAlias("profiles");
});


await app.RunAsync(args);
