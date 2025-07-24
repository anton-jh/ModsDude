using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Profiles;
internal class ProfileMenuCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    ProfileCollector profileCollector,
    EditProfileCommand editProfileCommand,
    DeleteProfileCommand deleteProfileCommand,
    IProfilesClient profilesClient)
    : ContextMenuCommand<ProfileMenuCommand.Settings>(ansiConsole)
{
    private RepoMembershipDto? _repo;
    private ProfileDto? _profile;


    protected override async Task<bool> Prepare(Settings settings, SelectionPrompt<ContextMenuChoice> menu, CancellationToken cancellationToken)
    {
        _repo = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Guest, cancellationToken);
        _profile = await profileCollector.Collect(default, _repo, cancellationToken);

        if (_profile is null)
        {
            _ansiConsole.NothingHere();
            return false;
        }

        menu.AddChoice(new("Edit", ContextMenuChoice.CommandReturnAction.Refresh,
            () => editProfileCommand.ExecuteAsync(new EditProfileCommand.Settings { RepoId = settings.RepoId, ProfileId = _profile.Id }, cancellationToken)));
        menu.AddChoice(new("Delete", ContextMenuChoice.CommandReturnAction.Return,
            () => deleteProfileCommand.ExecuteAsync(new DeleteProfileCommand.Settings { RepoId = settings.RepoId, ProfileId = _profile.Id }, cancellationToken)));

        return true;
    }

    protected override Task Refresh(Settings settings, CancellationToken cancellationToken)
    {
        if (_repo is null || _profile is null)
        {
            throw new InvalidOperationException();
        }

        return profilesClient.GetProfileV1Async(_repo.Repo.Id, _profile.Id, cancellationToken);
    }

    protected override void WriteHeader(Settings settings)
    {
        if (_repo is null || _profile is null)
        {
            throw new InvalidOperationException();
        }

        _ansiConsole.MarkupLineInterpolated($"[grey italic]({_repo.Repo.Id})[/] [blue bold]{_repo.Repo.Name}[/]");
        _ansiConsole.MarkupLineInterpolated($"  [grey italic]({_profile.Id})[/] [blue bold]{_profile.Name}[/]");
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; set; }

        [CommandOption("--profile-id")]
        public Guid ProfileId { get; set; }
    }
}
