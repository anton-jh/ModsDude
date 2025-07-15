using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Abstractions;

internal interface IInteractiveCommand
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
