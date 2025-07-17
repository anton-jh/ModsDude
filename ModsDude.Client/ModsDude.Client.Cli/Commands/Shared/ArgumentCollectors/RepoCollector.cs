using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Cli.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class RepoCollector(
    IAnsiConsole ansiConsole,
    IReposClient reposClient,
    IStateStore<State> stateStore)
{
    public async Task<RepoMembershipDto> Collect(Guid fromSettings, RepoMembershipLevel minimumLevel, CancellationToken cancellationToken)
    {
        IEnumerable<RepoMembershipDto> repoMemberships = await ansiConsole.Status()
            .StartAsync("Fetching repos...", _ => reposClient.GetMyReposV1Async(cancellationToken));

        repoMemberships = repoMemberships.Where(x => x.MembershipLevel >= minimumLevel);

        var selection = fromSettings != default
            ? repoMemberships.SingleOrDefault(x => x.Repo.Id == fromSettings)
            : null;

        if (fromSettings != default && selection is null)
        {
            ansiConsole.MarkupLineInterpolated($"[red]Repo with id '{fromSettings}' does not exist or you do not have sufficient access.[/]");
            ansiConsole.PressAnyKeyToContinue();
        }

        selection ??= await ansiConsole.PromptAsync(new SelectionPrompt<RepoMembershipDto>()
            .Title("Select repo")
            .WrapAround()
            .EnableSearch()
            .UseConverter(x => x.Repo.Name)
            .AddChoices(repoMemberships), cancellationToken);

        return selection;
    }
}
