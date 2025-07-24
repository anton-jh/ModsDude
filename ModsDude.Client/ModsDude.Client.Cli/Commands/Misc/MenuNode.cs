using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Misc;

internal abstract class MenuNode(string label, IAnsiConsole ansiConsole)
{
    protected readonly IAnsiConsole _ansiConsole = ansiConsole;

    public string Label { get; } = label;

    public abstract Task Select();
}
