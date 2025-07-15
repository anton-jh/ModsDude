using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class RepoCollector(
    IAnsiConsole ansiConsole,
    IReposClient reposClient)
{
    public async Task<RepoMembershipDto> Collect(Guid? fromSettings, RepoMembershipLevel minimumLevel, CancellationToken cancellationToken)
    {
        IEnumerable<RepoMembershipDto> repoMemberships = await ansiConsole.Status()
            .StartAsync("Fetching repos...", _ => reposClient.GetMyReposV1Async(cancellationToken));

        repoMemberships = repoMemberships.Where(x => x.MembershipLevel >= minimumLevel);

        var selection = repoMemberships.SingleOrDefault(x => x.Repo.Id == fromSettings);

        return await ansiConsole.PromptAsync(new SelectionPrompt<RepoMembershipDto>()
            .WrapAround()
            .EnableSearch()
            .UseConverter(x => x.Repo.Name)
            .AddChoices(repoMemberships), cancellationToken);
    }
}
