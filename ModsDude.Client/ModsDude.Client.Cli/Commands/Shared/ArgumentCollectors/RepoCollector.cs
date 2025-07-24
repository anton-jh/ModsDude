using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Cli.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class RepoCollector(
    IAnsiConsole ansiConsole,
    IReposClient reposClient,
    Store<State> store)
{
    public async Task<RepoMembershipDto?> Collect(Guid fromSettings, RepoMembershipLevel minimumLevel, CancellationToken cancellationToken)
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

        var lastSelectedIds = store.Get().LastSelectedRepos;

        var lastSelected = repoMemberships
            .Where(x => lastSelectedIds.Contains(x.Repo.Id))
            .OrderBy(x => lastSelectedIds.IndexOf(x.Repo.Id));
        var rest = repoMemberships
            .Where(x => !lastSelectedIds.Contains(x.Repo.Id))
            .OrderBy(x => x.Repo.Name)
            .ThenBy(x => x.Repo.Id);

        if (!lastSelected.Any() && !rest.Any())
        {
            return null;
        }

        var prompt = new SelectionPrompt<RepoMembershipDto>()
            .Title("Select repo")
            .WrapAround()
            .EnableSearch()
            .UseConverter(x => x.Repo.Name);

        prompt.AddChoices(lastSelected);
        prompt.AddChoices(rest);

        selection ??= await ansiConsole.PromptAsync(prompt, cancellationToken);

        UpdateLastSelected(selection);

        return selection;
    }


    private void UpdateLastSelected(RepoMembershipDto selection)
    {
        var recent = store.Get().LastSelectedRepos;

        recent.Remove(selection.Repo.Id);
        recent.Insert(0, selection.Repo.Id);

        if (recent.Count > 3)
        {
            recent.RemoveRange(3, recent.Count - 3);
        }

        store.Save();
    }
}
