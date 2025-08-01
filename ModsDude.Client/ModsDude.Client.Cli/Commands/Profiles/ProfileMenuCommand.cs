﻿using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Profiles;
internal class ProfileMenuCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    ProfileCollector profileCollector,
    IFactory<EditProfileCommand> editProfileCommand,
    IFactory<DeleteProfileCommand> deleteProfileCommand,
    IProfilesClient profilesClient)
    : ContextMenuCommand<ProfileMenuCommand.Settings>(ansiConsole)
{
    private RepoMembershipDto? _repo;
    private ProfileDto? _profile;


    protected override async Task<bool> Prepare(Settings settings, SelectionPrompt<ContextMenuChoice> menu, CancellationToken cancellationToken)
    {
        _ansiConsole.Clear();
        _repo = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Guest, cancellationToken);

        if (_repo is null)
        {
            _ansiConsole.NothingHere();
            return false;
        }

        _profile = await profileCollector.Collect(settings.ProfileId, _repo, cancellationToken);

        if (_profile is null)
        {
            _ansiConsole.NothingHere();
            return false;
        }

        menu.AddChoice(new("Activate", ContextMenuChoice.CommandReturnAction.None,
            () => _ansiConsole.Status().StartAsync("Activating...", _ => Task.Delay(2000))));

        if (_repo.MembershipLevel >= RepoMembershipLevel.Member)
        {
            menu.AddChoiceGroup(new("Manage"), [
                new("Edit", ContextMenuChoice.CommandReturnAction.Refresh,
                    () => editProfileCommand.Create().ExecuteAsync(new EditProfileCommand.Settings { RepoId = settings.RepoId, ProfileId = _profile.Id }, cancellationToken)),
                new("Delete", ContextMenuChoice.CommandReturnAction.Return,
                    () => deleteProfileCommand.Create().ExecuteAsync(new DeleteProfileCommand.Settings { RepoId = settings.RepoId, ProfileId = _profile.Id }, cancellationToken))
            ]);
        }

        return true;
    }

    protected override async Task Refresh(Settings settings, CancellationToken cancellationToken)
    {
        if (_repo is null || _profile is null)
        {
            throw new InvalidOperationException();
        }

        _profile = await profilesClient.GetProfileV1Async(_repo.Repo.Id, _profile.Id, cancellationToken);
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
