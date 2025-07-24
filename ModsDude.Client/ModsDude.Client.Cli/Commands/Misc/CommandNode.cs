using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Cli.Commands.Shared;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Misc;

internal class CommandNode<TCommand>(
    string label,
    IServiceProvider serviceProvider,
    IAnsiConsole ansiConsole)
    : MenuNode(label, ansiConsole)
    where TCommand : class, IInteractiveCommand
{
    public override async Task Select()
    {
        using var cts = new CancellationTokenSource();

        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            cts.Cancel();
            e.Cancel = true;
        }

        var commandInstance = serviceProvider.GetRequiredService<TCommand>();

        try
        {
            Console.CancelKeyPress += OnCancel;
            await commandInstance.ExecuteAsync(cts.Token);
        }
        catch (OperationCanceledException)
        { }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }
}
