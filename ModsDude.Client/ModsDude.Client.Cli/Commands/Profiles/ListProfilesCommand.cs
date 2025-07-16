using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Profiles;
internal class ListProfilesCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    IProfilesClient profilesClient)
    : AsyncCommandBase<ListProfilesCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var repoMembership = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Guest, cancellationToken);

        var profiles = await _ansiConsole.Status()
            .StartAsync("Fetching profiles...", _ => profilesClient.GetProfilesV1Async(repoMembership.Repo.Id, cancellationToken));

        var table = new Table();

        table.AddColumns(
            "Id", "Name");

        foreach (var profile in profiles)
        {
            table.AddRow(profile.Id.ToString(), profile.Name);
        }

        _ansiConsole.Clear();

        _ansiConsole.Write(table);
        _ansiConsole.WriteLine();

        _ansiConsole.PressAnyKeyToDismiss();
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; set; }
    }
}
