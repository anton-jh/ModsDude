using ModsDude.Client.Cli.Commands.Repos;
using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Profiles;
internal class ProfileMenuCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    ProfileCollector profileCollector,
    EditProfileCommand editProfileCommand,
    DeleteProfileCommand deleteProfileCommand,
    IProfilesClient profilesClient)
    : AsyncCommandBase<ProfileMenuCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var repo = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Guest, cancellationToken);
        var profile = await profileCollector.Collect(default, repo, cancellationToken);

        if (profile is null)
        {
            _ansiConsole.NothingHere();
            return;
        }

        var menuPrompt = new SelectionPrompt<CommandMenuOption>()
            .UseConverter(x => x.Label)
            .EnableSearch()
            .PageSize(20)
            .WrapAround();

        var backChoice = new CommandMenuOption("<- Back");
        menuPrompt.AddChoice(backChoice);

        menuPrompt.AddChoice(new("Edit", CommandMenuOption.CommandReturnAction.Refresh, () => editProfileCommand.ExecuteAsync(new EditProfileCommand.Settings { RepoId = settings.RepoId, ProfileId = profile.Id }, cancellationToken)));
        menuPrompt.AddChoice(new("Delete", CommandMenuOption.CommandReturnAction.Return, () => deleteProfileCommand.ExecuteAsync(new DeleteProfileCommand.Settings { RepoId = settings.RepoId, ProfileId = profile.Id }, cancellationToken)));

        while (true)
        {
            _ansiConsole.Clear();
            _ansiConsole.MarkupLineInterpolated($"[grey italic]({repo.Repo.Id})[/] [blue bold]{repo.Repo.Name}[/]");
            _ansiConsole.MarkupLineInterpolated($"  [grey italic]({profile.Id})[/] [blue bold]{profile.Name}[/]");

            var selection = await _ansiConsole.PromptAsync(menuPrompt, cancellationToken);

            if (selection == backChoice)
            {
                return;
            }

            await selection.Action.Invoke();

            if (selection.ReturnAction is CommandMenuOption.CommandReturnAction.Return)
            {
                return;
            }
            if (selection.ReturnAction is CommandMenuOption.CommandReturnAction.Refresh)
            {
                profile = await _ansiConsole.Status()
                    .StartAsync("Refreshing...", _ => profilesClient.GetProfileV1Async(repo.Repo.Id, profile.Id, cancellationToken));
            }
        }
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; set; }

        [CommandOption("--profile-id")]
        public Guid ProfileId { get; set; }
    }
}
