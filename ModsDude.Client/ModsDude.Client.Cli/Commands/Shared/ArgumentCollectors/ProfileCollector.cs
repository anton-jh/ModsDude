using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class ProfileCollector(
    IAnsiConsole ansiConsole,
    IProfilesClient profilesClient)
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
}
