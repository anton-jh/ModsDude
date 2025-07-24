using ModsDude.Client.Cli.Commands.Profiles;
using ModsDude.Client.Cli.Commands.Shared;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class RepoMenuCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    EditRepoCommand editRepoCommand,
    DeleteRepoCommand deleteRepoCommand,
    ProfileMenuCommand profileMenuCommand,
    IReposClient reposClient)
    : ContextMenuCommand<RepoMenuCommand.Settings>(ansiConsole)
{
    private RepoMembershipDto? _repo;


    protected override async Task<bool> Prepare(Settings settings, SelectionPrompt<ContextMenuChoice> menu, CancellationToken cancellationToken)
    {
        _repo = await repoCollector.Collect(default, RepoMembershipLevel.Guest, cancellationToken);

        menu.AddChoice(new("Profiles...", ContextMenuChoice.CommandReturnAction.None,
            () => profileMenuCommand.ExecuteAsync(new ProfileMenuCommand.Settings { RepoId = _repo.Repo.Id }, cancellationToken)));

        if (_repo.MembershipLevel >= RepoMembershipLevel.Admin)
        {
            menu.AddChoiceGroup(new("Admin"),
                new("Edit", ContextMenuChoice.CommandReturnAction.Refresh,
                    () => editRepoCommand.ExecuteAsync(new EditRepoCommand.Settings { RepoId = _repo.Repo.Id }, cancellationToken)),
                new("Delete", ContextMenuChoice.CommandReturnAction.Return,
                    () => deleteRepoCommand.ExecuteAsync(new DeleteRepoCommand.Settings { RepoId = _repo.Repo.Id }, cancellationToken)));
        }

        return true;
    }

    protected override Task Refresh(Settings settings, CancellationToken cancellationToken)
    {
        if (_repo is null)
        {
            throw new InvalidOperationException();
        }

        return reposClient.GetRepoDetailsV1Async(_repo.Repo.Id, cancellationToken);
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
