using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Profiles;
internal class DeleteProfileCommand(
    IAnsiConsole ansiConsole,
    ProfileCollector profileCollector,
    NameConfirmationCollector nameConfirmationCollector,
    IProfilesClient profilesClient)
    : AsyncCommandBase<DeleteProfileCommand.Settings>(ansiConsole)
{
    private const string _confirmationMessage = "[red]Are you sure you want to delete the following profile? This action is [bold]IRREVERSIBLE![/][/]";


    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var (repo, profile) = await profileCollector.Collect(settings.RepoId, settings.ProfileId, RepoMembershipLevel.Member, cancellationToken);

        if (repo is null || profile is null)
        {
            _ansiConsole.MarkupLine("[red]There are no profiles for you to delete.[/]");
            _ansiConsole.PressAnyKeyToDismiss();
            return;
        }

        var confirmation = await nameConfirmationCollector.Collect($"{repo.Repo.Name} / {profile.Name}", _confirmationMessage, cancellationToken);

        if (confirmation == false)
        {
            _ansiConsole.MarkupLine("[red]Action aborted.[/]");
            return;
        }

        await _ansiConsole.Status()
            .StartAsync($"Deleting profile '{repo.Repo.Name}'...", async ctx =>
            {
                await profilesClient.DeleteProfileV1Async(repo.Repo.Id, profile.Id, cancellationToken);
            });

        _ansiConsole.Clear();
        _ansiConsole.MarkupLine($"Profile '{profile.Name}' successfully deleted.");
        _ansiConsole.PressAnyKeyToDismiss();
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; init; }

        [CommandOption("--profile-id")]
        public Guid ProfileId { get; init; }
    }
}
