using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Cli.Authentication;
using ModsDude.Client.Cli.Commands.Misc;
using ModsDude.Client.Cli.Commands.Mods;
using ModsDude.Client.Cli.Commands.Profiles;
using ModsDude.Client.Cli.Commands.Repos;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.DependencyInjection;
using ModsDude.Client.Cli.DynamicForms;
using ModsDude.Client.Cli.Models;
using ModsDude.Client.Core.Extensions;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.Persistence;
using Spectre.Console;
using Spectre.Console.Cli;


var builder = Host.CreateDefaultBuilder(args);


builder.ConfigureLogging(logging =>
{
    logging.ClearProviders();
});

builder.ConfigureServices(static (ctx, services) =>
{
    services.AddCore<AuthenticationService>();
    services.AddSingleton<AuthenticationService>();
    services.AddSingleton(AnsiConsole.Console);
    services.AddSingleton<ClientConfiguration>();
    services.AddSingleton<FormPrompter>();
    services.AddSingleton<RepoCollector>();
    services.AddSingleton<ProfileCollector>();
    services.AddSingleton<NameConfirmationCollector>();
    services.AddSingleton<UserCollector>();
    services.AddSingleton<RepoMembershipLevelCollector>();
    services.AddSingleton(new Store<State>("cli.state.json"));
});


var registrar = new TypeRegistrar(builder);
var app = new CommandApp<ModListEditorCommand>(registrar);


app.Configure(config =>
{
    config.PropagateExceptions();

    config.AddCommand<OverviewCommand>("overview");
    config.AddCommand<ReloginCommand>("re-login").WithAlias("logout");
    config.AddCommand<CreateRepoCommand>("create-repo");

    config.AddCommand<RepoMenuCommand>("repo-menu").WithAlias("repo");
    config.AddCommand<EditRepoCommand>("edit-repo");
    config.AddCommand<DeleteRepoCommand>("delete-repo");
    config.AddCommand<RepoDetailsCommand>("inspect-repo");
    config.AddCommand<AddMemberCommand>("add-repo-member");
    config.AddCommand<CreateProfileCommand>("create-profile");

    config.AddCommand<ProfileMenuCommand>("profile-menu").WithAlias("profile");
    config.AddCommand<EditProfileCommand>("edit-profile");
    config.AddCommand<DeleteProfileCommand>("delete-profile");
});


await app.RunAsync(args);
