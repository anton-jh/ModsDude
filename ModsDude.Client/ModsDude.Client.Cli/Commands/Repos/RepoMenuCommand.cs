using ModsDude.Client.Cli.Commands.Profiles;
using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class RepoMenuCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    EditRepoCommand editRepoCommand,
    DeleteRepoCommand deleteRepoCommand,
    ProfileMenuCommand profileMenuCommand,
    IReposClient reposClient)
    : AsyncCommandBase<RepoMenuCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var repo = await repoCollector.Collect(default, RepoMembershipLevel.Guest, cancellationToken);

        var menuPrompt = new SelectionPrompt<CommandMenuOption>()
            .UseConverter(x => x.Label)
            .EnableSearch()
            .PageSize(20)
            .WrapAround();

        var backChoice = new CommandMenuOption("<- Back");
        menuPrompt.AddChoice(backChoice);

        menuPrompt.AddChoice(new(
            "Profiles...",
            CommandMenuOption.CommandReturnAction.None,
            () => profileMenuCommand.ExecuteAsync(new ProfileMenuCommand.Settings { RepoId = repo.Repo.Id }, cancellationToken)
        ));

        if (repo.MembershipLevel >= RepoMembershipLevel.Admin)
        {
            menuPrompt.AddChoiceGroup(new("Admin"),
                new("Edit", CommandMenuOption.CommandReturnAction.Refresh, () => editRepoCommand.ExecuteAsync(new EditRepoCommand.Settings { RepoId = repo.Repo.Id }, cancellationToken)),
                new("Delete", CommandMenuOption.CommandReturnAction.Return, () => deleteRepoCommand.ExecuteAsync(new DeleteRepoCommand.Settings { RepoId = repo.Repo.Id }, cancellationToken)));
        }

        while (true)
        {
            _ansiConsole.Clear();
            _ansiConsole.MarkupLineInterpolated($"[grey italic]({repo.Repo.Id})[/] [blue bold]{repo.Repo.Name}[/]");

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
                repo.Repo = await _ansiConsole.Status()
                    .StartAsync("Refreshing...", _ => reposClient.GetRepoDetailsV1Async(repo.Repo.Id, cancellationToken));
            }
        }
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; init; }
    }
}


internal record CommandMenuOption(string Label, CommandMenuOption.CommandReturnAction ReturnAction, Func<Task> Action)
{
    public CommandMenuOption(string Label)
        : this(Label, CommandReturnAction.None, () => Task.CompletedTask)
    {
    }


    public enum CommandReturnAction
    {
        None,
        Refresh,
        Return
    }
}
