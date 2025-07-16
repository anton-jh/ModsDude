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
    IReposClient reposClient)
    : AsyncCommandBase<DeleteRepoCommand.Settings>(ansiConsole)
{
    private const string _confirmationMessage = "[red]Are you sure you want to delete the following repo? This action is [bold]IRREVERSIBLE![/][/]";

    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var repoMembership = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Admin, cancellationToken);

        if (repoMembership is null)
        {
            _ansiConsole.MarkupLineInterpolated($"[red]Repo with id '{settings.RepoId}' does not exist or you are not authorized to delete it.[/]");
            _ansiConsole.PressAnyKeyToDismiss();
            return;
        }

        var confirmation = await CollectConfirmation(settings, repoMembership, cancellationToken);

        await _ansiConsole.Status()
            .StartAsync($"Deleting repo '{repoMembership.Repo.Name}'...", async ctx =>
            {
                await reposClient.DeleteRepoV1Async(repoMembership.Repo.Id, cancellationToken);
            });

        _ansiConsole.Clear();
        _ansiConsole.MarkupLine($"Repo '{repoMembership.Repo.Name}' successfully deleted.");
        _ansiConsole.PressAnyKeyToDismiss();
    }

    private async Task<bool> CollectConfirmation(Settings settings, RepoMembershipDto repoMembership, CancellationToken cancellationToken)
    {
        if (settings.RepoName is null || settings.RepoName != repoMembership.Repo.Name)
        {
            _ansiConsole.MarkupLine(_confirmationMessage);
            _ansiConsole.MarkupLineInterpolated($"[yellow]{repoMembership.Repo.Name}[/]");

            _ansiConsole.WriteLine();

            string? confirmation = null;
            while (confirmation != repoMembership.Repo.Name)
            {
                confirmation = await _ansiConsole.AskAsync<string>(
                    "Confirm by typing the name of the repo, or press Ctrl+C to cancel:", cancellationToken);
            }

            return true;
        }
        else
        {
            return await _ansiConsole.ConfirmAsync(
                prompt: $"{_confirmationMessage} ([yellow]{repoMembership.Repo.Name}[/])",
                defaultValue: false,
                cancellationToken: cancellationToken);
        }
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid? RepoId { get; init; }

        [CommandOption("--repo-name")]
        public string? RepoName { get; init; }
    }
}
