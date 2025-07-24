using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Misc;

internal class MiscNode(string label, IAnsiConsole ansiConsole)
    : MenuNode(label, ansiConsole)
{
    public override Task Select()
    {
        return Task.CompletedTask;
    }
}
