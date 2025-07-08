using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Cli.Commands;

internal sealed class GreetingCommand(IAnsiConsole console) : Command
{
    private readonly IAnsiConsole _console = console;

    public override int Execute(CommandContext context)
    {
        _console.MarkupLine("Hello, [bold]World[/]!");
        return 0;
    }
}
