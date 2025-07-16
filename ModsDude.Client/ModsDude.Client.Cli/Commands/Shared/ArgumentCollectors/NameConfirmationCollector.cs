using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class NameConfirmationCollector(
    IAnsiConsole ansiConsole)
{
    public async Task<bool> Collect(string name, string message, CancellationToken cancellationToken)
    {
        ansiConsole.MarkupLine(message);
        ansiConsole.MarkupLineInterpolated($"[yellow]{name}[/]");

        ansiConsole.WriteLine();

        string? confirmation = null;
        while (confirmation != name)
        {
            confirmation = await ansiConsole.AskAsync<string>(
                "Confirm by typing the name, or press Ctrl+C to cancel:", cancellationToken);
        }

        return true;
    }
}
