using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Shared;
internal abstract class ContextMenuCommand<TSettings>(IAnsiConsole ansiConsole)
    : AsyncCommandBase<TSettings>(ansiConsole)
    where TSettings : CommandSettings, new()
{
    public override async Task ExecuteAsync(TSettings settings, CancellationToken cancellationToken)
    {
        var backChoice = new ContextMenuChoice("<- Back");
        var menu = new SelectionPrompt<ContextMenuChoice>()
            .UseConverter(x => x.Label)
            .EnableSearch()
            .PageSize(20)
            .WrapAround()
            .AddChoices([backChoice]);

        if (!await Prepare(settings, menu, cancellationToken))
        {
            return;
        }

        while (true)
        {
            _ansiConsole.Clear();
            WriteHeader(settings);

            var selection = await _ansiConsole.PromptAsync(menu, cancellationToken);

            if (selection == backChoice)
            {
                return;
            }

            await selection.Action.Invoke();

            if (selection.ReturnAction is ContextMenuChoice.CommandReturnAction.Return)
            {
                return;
            }
            if (selection.ReturnAction is ContextMenuChoice.CommandReturnAction.Refresh)
            {
                await _ansiConsole.Status()
                    .StartAsync("Refreshing...", _ => Refresh(settings, cancellationToken));
            }
        }
    }


    protected abstract Task<bool> Prepare(TSettings settings, SelectionPrompt<ContextMenuChoice> menu, CancellationToken cancellationToken);
    protected abstract Task Refresh(TSettings settings, CancellationToken cancellationToken);
    protected abstract void WriteHeader(TSettings settings);
}


internal record ContextMenuChoice(string Label, ContextMenuChoice.CommandReturnAction ReturnAction, Func<Task> Action)
{
    public ContextMenuChoice(string Label)
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
