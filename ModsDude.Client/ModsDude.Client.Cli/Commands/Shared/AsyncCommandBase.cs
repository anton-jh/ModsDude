using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Shared;
internal abstract class AsyncCommandBase<TSettings>(
    IAnsiConsole ansiConsole)
    : AsyncCommand<TSettings>, IInteractiveCommand
    where TSettings : CommandSettings, new()
{
    protected readonly IAnsiConsole _ansiConsole = ansiConsole;


    public override async Task<int> ExecuteAsync(CommandContext context, TSettings settings)
    {
        await ExecuteAsync(settings, default);

        return 0;
    }

    public virtual async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(new(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ansiConsole.Clear();

            _ansiConsole.MarkupLine("[red]Oops![/] [yellow]Seems like something went wrong.[/]");
            _ansiConsole.WriteLine();

            if (ex is ApiException<CustomProblemDetails> apiEx)
            {
                _ansiConsole.WriteLine(apiEx.Result.Title ?? "Unknown error");
            }

            _ansiConsole.WriteException(ex);
            _ansiConsole.WriteLine();

            _ansiConsole.PressAnyKeyToDismiss();
        }
    }

    public abstract Task ExecuteAsync(TSettings settings, CancellationToken cancellationToken);
}
