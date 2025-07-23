using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class AddMemberCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    UserCollector userCollector,
    RepoMembershipLevelCollector repoMembershipLevelCollector,
    IReposClient reposClient,
    IMembersClient membersClient)
    : AsyncCommandBase<AddMemberCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var repoMembership = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Member, cancellationToken);

        var repoDetails = await _ansiConsole.Status()
            .StartAsync("Fetching repo...", _ => reposClient.GetRepoDetailsV1Async(repoMembership.Repo.Id, cancellationToken));

        var selectedUser = await userCollector.Collect(settings.SearchUsername, repoDetails.Members.Select(x => x.User.Id), cancellationToken);

        if (repoDetails.Members.Any(x => x.User.Id == selectedUser.Id))
        {
            _ansiConsole.MarkupLineInterpolated($"[red]User '{selectedUser.Username}' is already a member of '{repoMembership.Repo.Name}'.[/]");
            _ansiConsole.WriteLine();
            _ansiConsole.PressAnyKeyToDismiss();
            return;
        }

        var selectedLevel = await repoMembershipLevelCollector.Collect(settings.MembershipLevel, repoMembership.MembershipLevel, cancellationToken);

        await _ansiConsole.Status()
            .StartAsync("Adding member...", _ => membersClient.AddMemberV1Async(repoMembership.Repo.Id, new()
            {
                UserId = selectedUser.Id,
                MembershipLevel = selectedLevel
            }, cancellationToken));
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; set; }

        [CommandOption("--search-username")]
        public string? SearchUsername { get; set; }

        [CommandOption("--membership-level")]
        public RepoMembershipLevel? MembershipLevel { get; set; }
    }
}
