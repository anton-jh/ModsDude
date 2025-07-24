using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Profiles;
internal class CreateProfileCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    IProfilesClient profilesClient,
    IFactory<ProfileMenuCommand> profileMenuCommandFactory)
    : AsyncCommandBase<CreateProfileCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var repoMembership = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Member, cancellationToken);

        if (repoMembership is null)
        {
            _ansiConsole.NothingHere();
            return;
        }

        var name = await CollectName(settings, repoMembership, cancellationToken);

        var profile = await _ansiConsole.Status()
            .StartAsync("Creating profile...", _ => profilesClient.CreateProfileV1Async(repoMembership.Repo.Id, new()
            {
                Name = name
            }));

        _ansiConsole.Clear();

        _ansiConsole.MarkupLine("Profile successfully created.");
        _ansiConsole.WriteLine();
        _ansiConsole.PressAnyKeyToDismiss();

        await profileMenuCommandFactory.Create().ExecuteAsync(new ProfileMenuCommand.Settings { RepoId = repoMembership.Repo.Id, ProfileId = profile.Id }, cancellationToken);
    }


    private async Task<string> CollectName(Settings settings, RepoMembershipDto repoMembership, CancellationToken cancellationToken)
    {
        var profiles = await _ansiConsole.Status()
            .StartAsync("Fetching profiles...", _ => profilesClient.GetProfilesV1Async(repoMembership.Repo.Id, cancellationToken));

        var nameTaken = false;
        var name = settings.Name;

        while (nameTaken || string.IsNullOrWhiteSpace(name))
        {
            _ansiConsole.Clear();
            if (nameTaken)
            {
                _ansiConsole.MarkupLineInterpolated($"[red]Name '{name}' taken.[/]");
            }
            var prompt = new TextPrompt<string>("[yellow]Name:[/]");

            name = await _ansiConsole.PromptAsync(prompt, cancellationToken);
            name = name.Trim();

            nameTaken = profiles.Any(x => x.Name == name);
        }

        return name;
    }


    public class Settings : CommandSettings
    {
        public Guid RepoId { get; init; }
        public string? Name { get; init; }
    }
}
