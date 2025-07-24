using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Profiles;
internal class EditProfileCommand(
    IAnsiConsole ansiConsole,
    ProfileCollector profileCollector,
    IProfilesClient profilesClient)
    : AsyncCommandBase<EditProfileCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var (repo, profile) = await profileCollector.Collect(settings.RepoId, settings.ProfileId, RepoMembershipLevel.Member, cancellationToken);

        if (repo is null || profile is null)
        {
            _ansiConsole.NothingHere();
            return;
        }

        var newName = await CollectName(settings, profile.Name, repo, cancellationToken);

        if (newName != profile.Name)
        {
            await _ansiConsole.Status()
                .StartAsync("Updating profile...", _ => profilesClient.UpdateProfileV1Async(repo.Repo.Id, profile.Id, new()
                {
                    Name = newName
                }));
        }

        _ansiConsole.Clear();

        _ansiConsole.MarkupLine("Profile successfully updated.");
        _ansiConsole.WriteLine();
        _ansiConsole.PressAnyKeyToDismiss();
    }


    private async Task<string> CollectName(Settings settings, string current, RepoMembershipDto repoMembership, CancellationToken cancellationToken)
    {
        var profiles = await _ansiConsole.Status()
            .StartAsync("Fetching profiles...", _ => profilesClient.GetProfilesV1Async(repoMembership.Repo.Id, cancellationToken));

        var nameTaken = false;
        var name = settings.SetName;

        while (nameTaken || string.IsNullOrWhiteSpace(name))
        {
            _ansiConsole.Clear();
            if (nameTaken)
            {
                _ansiConsole.MarkupLineInterpolated($"[red]Name '{name}' taken.[/]");
            }
            var prompt = new TextPrompt<string>("[yellow]Name:[/]")
                .DefaultValue(current);

            name = await _ansiConsole.PromptAsync(prompt, cancellationToken);
            name = name.Trim();

            nameTaken = name != current && profiles.Any(x => x.Name == name);
        }

        return name;
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; init; }

        [CommandOption("--profile-id")]
        public Guid ProfileId { get; init; }

        [CommandOption("--set-name")]
        public string? SetName { get; init; }
    }
}
