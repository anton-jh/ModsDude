using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Shared;

internal interface IInteractiveCommand
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
