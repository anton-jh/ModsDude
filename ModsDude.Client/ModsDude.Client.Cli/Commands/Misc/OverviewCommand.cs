using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Misc;
internal class OverviewCommand(
    IAnsiConsole ansiConsole,
    IReposClient reposClient,
    IProfilesClient profilesClient)
    : AsyncCommandBase<EmptyCommandSettings>(ansiConsole)
{
    public override async Task ExecuteAsync(EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        var repos = await _ansiConsole.Status()
            .StartAsync("Fetching repos..", _ => reposClient.GetMyReposV1Async(cancellationToken));

        List<ProfileDto> profiles = [];

        foreach (var repo in repos)
        {
            var subCollection = await _ansiConsole.Status()
                .StartAsync("Fetching profiles...", _ => profilesClient.GetProfilesV1Async(repo.Repo.Id, cancellationToken));
            profiles.AddRange(subCollection);
        }

        var root = new Tree("Repos and profiles");

        foreach (var repo in repos)
        {
            var repoNode = new Tree(repo.Repo.Name);

            repoNode.AddNodes(profiles
                .Where(x => x.RepoId == repo.Repo.Id)
                .Select(x => new Tree(x.Name)));

            root.AddNode(repoNode);
        }

        _ansiConsole.Clear();
        _ansiConsole.Write(root);

        _ansiConsole.WriteLine();
        _ansiConsole.PressAnyKeyToDismiss();
    }
}
