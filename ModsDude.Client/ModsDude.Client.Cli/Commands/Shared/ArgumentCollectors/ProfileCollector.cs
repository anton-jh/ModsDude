using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Cli.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class ProfileCollector(
    IAnsiConsole ansiConsole,
    IProfilesClient profilesClient,
    IReposClient reposClient,
    Store<State> store)
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

        var prompt = new SelectionPrompt<ProfileDto>()
            .Title("Select profile")
            .WrapAround()
            .EnableSearch()
            .UseConverter(x => x.Name);

        var lastSelected = GetLastSelected(profiles);

        if (lastSelected.Any())
        {
            prompt.AddChoiceGroup(new ProfileDto() { Name = "Recent" }, lastSelected);
        }

        prompt.AddChoiceGroup(new ProfileDto() { Name = "All" }, profiles);

        selection ??= await ansiConsole.PromptAsync(prompt, cancellationToken);

        return selection;
    }

    public async Task<(RepoMembershipDto? Repo, ProfileDto? Profile)> Collect(Guid repoIdFromSettings, Guid profileIdFromSettings, RepoMembershipLevel minimumLevel, CancellationToken cancellationToken)
    {
        RepoMembershipDto? repo;
        ProfileDto? profile;

        var repoMemberships = await ansiConsole.Status()
            .StartAsync("Fetching repos...", _ => reposClient.GetMyReposV1Async(cancellationToken));

        repoMemberships = repoMemberships.Where(x => x.MembershipLevel >= minimumLevel).ToList();


        if (repoIdFromSettings != default)
        {
            (repo, profile) = await CollectFromRepo(repoIdFromSettings, profileIdFromSettings, repoMemberships, cancellationToken);
            if (repo is not null && profile is not null)
            {
                return (repo, profile);
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
            return (null, null);
        }

        profile = await ansiConsole.PromptAsync(prompt, cancellationToken);
        repo = repoMemberships.Single(x => x.Repo.Id == profile.RepoId);

        return new(repo, profile);
    }

    private async Task<(RepoMembershipDto? Repo, ProfileDto? Profile)> CollectFromRepo(Guid repoIdFromSettings, Guid profileIdFromSettings, ICollection<RepoMembershipDto> repoMemberships, CancellationToken cancellationToken)
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
        return (null, null);
    }


    private IEnumerable<ProfileDto> GetLastSelected(IEnumerable<ProfileDto> all)
    {
        var lastSelected = store.Get().LastSelectedProfiles;

        foreach (var id in lastSelected)
        {
            if (all.SingleOrDefault(x => x.Id == id) is ProfileDto profile)
            {
                yield return profile;
            }
        }
    }

    private void UpdateLastSelected(ProfileDto selection)
    {
        var recent = store.Get().LastSelectedProfiles;

        recent.Insert(0, selection.Id);
        if (recent.Count > 3)
        {
            recent.RemoveRange(3, recent.Count - 3);
        }

        store.Save();
    }
}
