using ModsDude.Client.Cli.Commands.Profiles;
using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class RepoMenuCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    IFactory<EditRepoCommand> editRepoCommand,
    IFactory<DeleteRepoCommand> deleteRepoCommand,
    IFactory<CreateProfileCommand> createProfileCommandFactory,
    IFactory<ProfileMenuCommand> profileMenuCommandFactory,
    IFactory<RepoDetailsCommand> repoDetailsCommandFactory,
    IFactory<AddMemberCommand> addMemberCommandFactory,
    IReposClient reposClient)
    : ContextMenuCommand<RepoMenuCommand.Settings>(ansiConsole)
{
    private RepoMembershipDto? _repo;


    protected override async Task<bool> Prepare(Settings settings, SelectionPrompt<ContextMenuChoice> menu, CancellationToken cancellationToken)
    {
        _ansiConsole.Clear();
        _repo = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Guest, cancellationToken);

        if (_repo is null)
        {
            _ansiConsole.NothingHere();
            return false;
        }

        menu.AddChoice(new("Details", ContextMenuChoice.CommandReturnAction.None,
            () => repoDetailsCommandFactory.Create().ExecuteAsync(new RepoDetailsCommand.Settings { RepoId = _repo.Repo.Id }, cancellationToken)));

        var profileChoices = new List<ContextMenuChoice>
        {
            new("Profiles...", ContextMenuChoice.CommandReturnAction.None,
                () => profileMenuCommandFactory.Create().ExecuteAsync(new ProfileMenuCommand.Settings { RepoId = _repo.Repo.Id }, cancellationToken))
        };

        if (_repo.MembershipLevel >= RepoMembershipLevel.Member)
        {
            profileChoices.Add(new("New profile", ContextMenuChoice.CommandReturnAction.None,
                () => createProfileCommandFactory.Create().ExecuteAsync(new CreateProfileCommand.Settings { RepoId = _repo.Repo.Id }, cancellationToken)));
        }

        menu.AddChoiceGroup(new("Profiles"), profileChoices);

        if (_repo.MembershipLevel >= RepoMembershipLevel.Member)
        {
            menu.AddChoiceGroup(new("Members"), [
                new("Invite", ContextMenuChoice.CommandReturnAction.None,
                    () => addMemberCommandFactory.Create().ExecuteAsync(new AddMemberCommand.Settings { RepoId = _repo.Repo.Id }, cancellationToken))
            ]);
        }

        if (_repo.MembershipLevel >= RepoMembershipLevel.Admin)
        {
            menu.AddChoiceGroup(new("Admin"), [
                new("Edit", ContextMenuChoice.CommandReturnAction.Refresh,
                    () => editRepoCommand.Create().ExecuteAsync(new EditRepoCommand.Settings { RepoId = _repo.Repo.Id }, cancellationToken)),
                new("Delete", ContextMenuChoice.CommandReturnAction.Return,
                    () => deleteRepoCommand.Create().ExecuteAsync(new DeleteRepoCommand.Settings { RepoId = _repo.Repo.Id }, cancellationToken))
            ]);
        }

        return true;
    }

    protected override async Task Refresh(Settings settings, CancellationToken cancellationToken)
    {
        if (_repo is null)
        {
            throw new InvalidOperationException();
        }

        _repo.Repo = await reposClient.GetRepoDetailsV1Async(_repo.Repo.Id, cancellationToken);
    }

    protected override void WriteHeader(Settings settings)
    {
        if (_repo is null)
        {
            throw new InvalidOperationException();
        }

        _ansiConsole.MarkupLineInterpolated($"[grey italic]({_repo.Repo.Id})[/] [blue bold]{_repo.Repo.Name}[/]");
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid RepoId { get; init; }
    }
}
