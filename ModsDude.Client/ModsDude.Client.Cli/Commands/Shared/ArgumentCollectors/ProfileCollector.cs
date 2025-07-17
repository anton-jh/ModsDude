using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class ProfileCollector(
    IAnsiConsole ansiConsole,
    IProfilesClient profilesClient,
    IReposClient reposClient)
{
    public async Task<ProfileDto?> Collect(Guid fromSettings, RepoMembershipDto repoMembership, CancellationToken cancellationToken)
    {
        var profiles = await ansiConsole.Status()
            .StartAsync("Fetching profiles...", _ => profilesClient.GetProfilesV1Async(repoMembership.Repo.Id, cancellationToken));

        if (profiles.Count == 0)
        {
            return null;
        }

        var selection = fromSettings != default
            ? profiles.SingleOrDefault(x => x.Id == fromSettings)
            : null;

        if (fromSettings != default && selection is null)
        {
            ansiConsole.MarkupLineInterpolated($"[red]Profile with id '{fromSettings}' does not exist in repo with id '{repoMembership.Repo.Id}'.[/]");
            ansiConsole.PressAnyKeyToContinue();
        }

        selection ??= await ansiConsole.PromptAsync(new SelectionPrompt<ProfileDto>()
            .Title("Select profile")
            .WrapAround()
            .EnableSearch()
            .UseConverter(x => x.Name)
            .AddChoices(profiles), cancellationToken);

        return selection;
    }

    public async Task<ProfileDtoWithRepo?> Collect(Guid repoIdFromSettings, Guid profileIdFromSettings, RepoMembershipLevel minimumLevel, CancellationToken cancellationToken)
    {
        var repoMemberships = await ansiConsole.Status()
            .StartAsync("Fetching repos...", _ => reposClient.GetMyReposV1Async(cancellationToken));

        repoMemberships = repoMemberships.Where(x => x.MembershipLevel >= minimumLevel).ToList();


        if (repoIdFromSettings != default)
        {
            var result = await CollectFromRepo(repoIdFromSettings, profileIdFromSettings, repoMemberships, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        var prompt = new SelectionPrompt<ProfileDto>()
            .Title("Select profile")
            .PageSize(20)
            .WrapAround()
            .EnableSearch()
            .UseConverter(x => x.Name)
            .Mode(SelectionMode.Leaf);

        var anyChoices = false;

        await ansiConsole.Status()
            .StartAsync("Fetching profiles...", async _ =>
            {
                foreach (var repo in repoMemberships)
                {
                    var profiles = await profilesClient.GetProfilesV1Async(repo.Repo.Id, cancellationToken);
                    if (profiles.Count != 0)
                    {
                        anyChoices = true;
                        prompt.AddChoiceGroup(
                            new ProfileDto() { Id = default, Name = repo.Repo.Name, RepoId = repo.Repo.Id },
                            profiles);
                    }
                }
            });

        if (!anyChoices)
        {
            return null;
        }

        var profile = await ansiConsole.PromptAsync(prompt, cancellationToken);
        var repo = repoMemberships.Single(x => x.Repo.Id == profile.RepoId);

        return new(repo, profile);
    }

    private async Task<ProfileDtoWithRepo?> CollectFromRepo(Guid repoIdFromSettings, Guid profileIdFromSettings, ICollection<RepoMembershipDto> repoMemberships, CancellationToken cancellationToken)
    {
        var repo = repoMemberships.SingleOrDefault(x => x.Repo.Id == repoIdFromSettings);
        if (repo is not null)
        {
            var profile = await Collect(profileIdFromSettings, repo, cancellationToken);
            if (profile is not null)
            {
                return new(repo, profile);
            }
            ansiConsole.MarkupLineInterpolated($"[red]Repo with id '{repoIdFromSettings}' does not have any profiles.[/]");
            ansiConsole.PressAnyKeyToContinue();
        }
        else
        {
            ansiConsole.MarkupLineInterpolated($"[red]Repo with id '{repoIdFromSettings}' does not exist or you do not have sufficient access.[/]");
            ansiConsole.PressAnyKeyToContinue();
        }
        return null;
    }
}
