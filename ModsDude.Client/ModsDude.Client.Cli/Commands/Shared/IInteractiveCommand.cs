using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Shared;

internal interface IInteractiveCommand
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}

internal interface IInteractiveCommand<TSettings>
{
    Task ExecuteAsync(TSettings settings, CancellationToken cancellationToken);
}
