using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands;
internal class ListReposCommand(IReposClient reposClient, IAnsiConsole ansiConsole)
    : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var repoMemberships = await reposClient.GetMyReposV1Async();

        var table = new Table();

        table.AddColumns(
            new TableColumn("Id"),
            new TableColumn("Name"));

        foreach (var repoMembership in repoMemberships)
        {
            table.AddRow(
                repoMembership.Repo.Id.ToString(),
                repoMembership.Repo.Name,
                repoMembership.MembershipLevel.ToString());
        }

        ansiConsole.Write(table);

        return 0;
    }
}
