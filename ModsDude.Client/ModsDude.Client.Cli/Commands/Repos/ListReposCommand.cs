using ModsDude.Client.Cli.Commands.Abstractions;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class ListReposCommand(IReposClient reposClient, IAnsiConsole ansiConsole)
    : AsyncCommandBase<EmptyCommandSettings>(ansiConsole)
{
    public override async Task ExecuteAsync(EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        var repoMemberships = await _ansiConsole.Status()
            .StartAsync("Fetching repos...", _ => reposClient.GetMyReposV1Async(cancellationToken));

        var table = new Table();

        table.AddColumns(
            new TableColumn("Id"),
            new TableColumn("Name"),
            new TableColumn("Membership level"));

        foreach (var repoMembership in repoMemberships)
        {
            table.AddRow(
                repoMembership.Repo.Id.ToString(),
                repoMembership.Repo.Name,
                repoMembership.MembershipLevel.ToString());
        }

        _ansiConsole.Clear();

        _ansiConsole.Write(table);
        _ansiConsole.WriteLine();

        _ansiConsole.PressAnyKeyToDismiss();
    }
}
