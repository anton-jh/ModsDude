using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal interface IRepoCommandSettings
{
    [CommandOption("--repo-id")]
    Guid RepoId { get; init; }
}
