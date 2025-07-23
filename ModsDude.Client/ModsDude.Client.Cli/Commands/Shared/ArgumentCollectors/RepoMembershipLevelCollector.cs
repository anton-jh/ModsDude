using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class RepoMembershipLevelCollector
    (IAnsiConsole ansiConsole)
{
    public async Task<RepoMembershipLevel> Collect(RepoMembershipLevel? fromSettings, RepoMembershipLevel maxLevel, CancellationToken cancellationToken)
    {
        if (fromSettings is not null && fromSettings <= maxLevel)
        {
            return fromSettings.Value;
        }

        var prompt = new TextPrompt<RepoMembershipLevel>("Membership level:")
            .WithConverter(x => x.ToString())
            .AddChoice(RepoMembershipLevel.Guest);

        if (maxLevel >= RepoMembershipLevel.Member)
        {
            prompt.AddChoice(RepoMembershipLevel.Member);
        }
        if (maxLevel >= RepoMembershipLevel.Admin)
        {
            prompt.AddChoice(RepoMembershipLevel.Admin);
        }

        var result = await ansiConsole.PromptAsync(prompt, cancellationToken);

        return result;
    }
}
