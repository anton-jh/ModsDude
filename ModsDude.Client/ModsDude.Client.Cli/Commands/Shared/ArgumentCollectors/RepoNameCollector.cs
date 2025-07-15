using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class RepoNameCollector(
    IAnsiConsole ansiConsole,
    IReposClient reposClient)
{
    public async Task<string> Collect(string? fromSettings, bool runFromMenu, CancellationToken cancellationToken)
    {
        var nameTaken = false;
        var name = fromSettings;

        while (nameTaken || string.IsNullOrWhiteSpace(name))
        {
            ansiConsole.If(runFromMenu)?.Clear();
            if (nameTaken)
            {
                ansiConsole.MarkupLineInterpolated($"[red]Name '{name}' taken.[/]");
            }
            name ??= await ansiConsole.PromptAsync(new TextPrompt<string>("[yellow]Give the repo a friendly name:[/]"), cancellationToken);
            name = name.Trim();

            var nameTakenResult = await ansiConsole.Status()
                .StartAsync("Checking so the name is not taken...", _ => reposClient.CheckNameTakenV1Async(new() { Name = name }, cancellationToken));

            nameTaken = nameTakenResult.IsTaken;
        }

        return name;
    }
}
