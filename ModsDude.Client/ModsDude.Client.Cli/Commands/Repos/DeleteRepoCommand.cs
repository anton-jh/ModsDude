using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class DeleteRepoCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    NameConfirmationCollector nameConfirmationCollector,
    IReposClient reposClient)
    : AsyncCommandBase<DeleteRepoCommand.Settings>(ansiConsole)
{
    private const string _confirmationMessage = "[red]Are you sure you want to delete the following repo? This action is [bold]IRREVERSIBLE![/][/]";

    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var repoMembership = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Admin, cancellationToken);

        var confirmation = await nameConfirmationCollector.Collect(repoMembership.Repo.Name, _confirmationMessage, cancellationToken);

        if (confirmation == false)
        {
            _ansiConsole.MarkupLine("[red]Action aborted.[/]");
            return;
        }

        await _ansiConsole.Status()
            .StartAsync($"Deleting repo '{repoMembership.Repo.Name}'...", async ctx =>
            {
                await reposClient.DeleteRepoV1Async(repoMembership.Repo.Id, cancellationToken);
            });

        _ansiConsole.Clear();
        _ansiConsole.MarkupLine($"Repo '{repoMembership.Repo.Name}' successfully deleted.");
        _ansiConsole.PressAnyKeyToDismiss();
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; init; }
    }
}
