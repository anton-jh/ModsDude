using ModsDude.Client.Cli.Commands.Abstractions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class CreateRepoCommand(
    IAnsiConsole ansiConsole,
    IReposClient reposClient)
    : AsyncCommandBase<CreateRepoCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings)
    {
        var name = settings.Name ?? _ansiConsole.Prompt(new TextPrompt<string>("Give the repo a friendly name:"));
        name = name.Trim();

        await reposClient.CreateRepoV1Async(new()
        {
            Name = name,
            AdapterId = "",
            AdapterConfiguration = "",
        });
    }

    public class Settings : CommandSettings
    {
        [CommandOption("-n|--name")]
        public string? Name { get; init; }
    }
}
