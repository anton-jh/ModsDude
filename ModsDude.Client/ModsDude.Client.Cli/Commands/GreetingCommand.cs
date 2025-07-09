using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Cli.Commands;

internal sealed class GreetingCommand(IAnsiConsole console) : Command
{
    private readonly IAnsiConsole _console = console;

    public override int Execute(CommandContext context)
    {
        var loop = true;

        while (loop)
        {
            var name = _console.Prompt(new TextPrompt<string>("What's your [green]name[/]?"));
            _console.MarkupLine($"[yellow]Hello[/], [bold][red]{name}[/][/]!");

            loop = _console.Prompt(new TextPrompt<bool>("Should I ask again?")
                .AddChoice(true)
                .AddChoice(false)
                .DefaultValue(false)
                .WithConverter(x => x ? "y" : "n"));
        }
        return 0;
    }
}
