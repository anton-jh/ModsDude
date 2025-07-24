using ModsDude.Client.Cli.Commands.Repos;
using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Core.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Misc;

internal class MainMenuCommand(
    IAnsiConsole ansiConsole,
    IFactory<OverviewCommand> overviewCommandFactory,
    IFactory<RepoMenuCommand> repoMenuCommandFactory,
    IFactory<CreateRepoCommand> createRepoCommandFactory,
    IFactory<ReloginCommand> reloginCommandFactory)
    : ContextMenuCommand<EmptyCommandSettings>(ansiConsole)
{
    protected override Task<bool> Prepare(EmptyCommandSettings settings, SelectionPrompt<ContextMenuChoice> menu, CancellationToken cancellationToken)
    {
        menu.AddChoice(new("Overview", ContextMenuChoice.CommandReturnAction.None,
            () => overviewCommandFactory.Create().ExecuteAsync(cancellationToken)));

        menu.AddChoiceGroup(new("Repos"), [
            new("Repos...", ContextMenuChoice.CommandReturnAction.None, () => repoMenuCommandFactory.Create().ExecuteAsync(cancellationToken)),
            new("New repo", ContextMenuChoice.CommandReturnAction.None, () => createRepoCommandFactory.Create().ExecuteAsync(cancellationToken))
        ]);

        menu.AddChoiceGroup(new("Misc"), [
            new("Re-login / change user", ContextMenuChoice.CommandReturnAction.None, () => reloginCommandFactory.Create().ExecuteAsync(cancellationToken))
        ]);

        return Task.FromResult(true);
    }

    protected override Task Refresh(EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override void WriteHeader(EmptyCommandSettings settings)
    {
        _ansiConsole.MarkupLine("[blue bold]Main menu[/]");
    }
}
