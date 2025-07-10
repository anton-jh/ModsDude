using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Abstractions;

internal interface IInteractiveCommand
{
    Task ExecuteAsync();
}

internal interface IInteractiveCommand<TSettings> : IInteractiveCommand
    where TSettings : CommandSettings, new()
{
    Task ExecuteAsync(TSettings settings);
}
