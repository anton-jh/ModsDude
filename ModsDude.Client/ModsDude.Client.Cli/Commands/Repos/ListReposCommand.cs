using ModsDude.Client.Cli.Commands.Abstractions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class ListReposCommand(IReposClient reposClient, IAnsiConsole ansiConsole)
    : AsyncCommandBase<EmptyCommandSettings>(ansiConsole)
{
    public override async Task ExecuteAsync(EmptyCommandSettings _)
    {
        var repoMemberships = await reposClient.GetMyReposV1Async();

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

        _ansiConsole.MarkupLine("[blue]<- Press any key to dismiss.[/]");
        Console.ReadKey(true);
    }
}
