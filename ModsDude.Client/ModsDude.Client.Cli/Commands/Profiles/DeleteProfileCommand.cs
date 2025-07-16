using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Profiles;
internal class DeleteProfileCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    ProfileCollector profileCollector,
    NameConfirmationCollector nameConfirmationCollector,
    IProfilesClient profilesClient)
    : AsyncCommandBase<DeleteProfileCommand.Settings>(ansiConsole)
{
    private const string _confirmationMessage = "[red]Are you sure you want to delete the following profile? This action is [bold]IRREVERSIBLE![/][/]";


    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var repoMembership = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Admin, cancellationToken);
        var profile = await profileCollector.Collect(settings.ProfileId, repoMembership, cancellationToken);

        if (profile is null)
        {
            _ansiConsole.MarkupLine("[red]Repo does not have any profiles.[/]");
            _ansiConsole.PressAnyKeyToDismiss();
            return;
        }

        var confirmation = await nameConfirmationCollector.Collect($"{repoMembership.Repo.Name} / {profile.Name}", _confirmationMessage, cancellationToken);

        if (confirmation == false)
        {
            _ansiConsole.MarkupLine("[red]Action aborted.[/]");
            return;
        }

        await _ansiConsole.Status()
            .StartAsync($"Deleting profile '{repoMembership.Repo.Name}'...", async ctx =>
            {
                await profilesClient.DeleteProfileV1Async(repoMembership.Repo.Id, profile.Id, cancellationToken);
            });

        _ansiConsole.Clear();
        _ansiConsole.MarkupLine($"Profile '{repoMembership.Repo.Name}' successfully deleted.");
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
