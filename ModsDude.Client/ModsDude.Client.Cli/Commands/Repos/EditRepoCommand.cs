using ModsDude.Client.Cli.Commands.Abstractions;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.DynamicForms;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class EditRepoCommand(
    IAnsiConsole ansiConsole,
    RepoCollector repoCollector,
    IGameAdapterIndex gameAdapterIndex,
    FormPrompter formPrompter,
    IReposClient reposClient)
    : AsyncCommandBase<EditRepoCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var repoMembership = await repoCollector.Collect(settings.RepoId, RepoMembershipLevel.Admin, cancellationToken);

        var newName = await CollectName(settings, repoMembership.Repo.Name, cancellationToken);

        var gameAdapter = gameAdapterIndex.GetById(GameAdapterId.Parse(repoMembership.Repo.AdapterId));
        var baseSettings = gameAdapter.DeserializeBaseSettings(repoMembership.Repo.AdapterConfiguration);

        var baseSettingsChanged = await formPrompter.Prompt(
            form: baseSettings,
            title: "[blue bold]Edit base settings for the game adapter[/] (Submit empty to keep current value)\n" +
                   "[blue]Note:[/] Game adapters may support editing [italic]all[/], [italic]some[/] or [italic]none[/] or their settings.",
            onlyModify: true,
            cancellationToken: cancellationToken);

        if (newName != repoMembership.Repo.Name || baseSettingsChanged)
        {
            await _ansiConsole.Status()
                .StartAsync("Saving changes...", _ => reposClient.UpdateRepoV1Async(repoMembership.Repo.Id, new()
                {
                    Name = newName,
                    AdapterConfiguration = baseSettings.Serialize()
                }, cancellationToken));
        }

        _ansiConsole.Clear();

        _ansiConsole.MarkupLine("Repo successfully updated.");
        _ansiConsole.WriteLine();
        _ansiConsole.PressAnyKeyToDismiss();
    }


    private async Task<string> CollectName(Settings settings, string current, CancellationToken cancellationToken)
    {
        var nameTaken = false;
        var name = settings.SetName;

        while (nameTaken || string.IsNullOrWhiteSpace(name))
        {
            _ansiConsole.Clear();
            if (nameTaken)
            {
                _ansiConsole.MarkupLineInterpolated($"[red]Name '{name}' taken.[/]");
            }
            var prompt = new TextPrompt<string>("[yellow]Give the repo a new friendly name:[/]")
                .DefaultValue(current);

            name = await _ansiConsole.PromptAsync(prompt, cancellationToken);
            name = name.Trim();

            if (name != current)
            {
                var nameTakenResult = await _ansiConsole.Status()
                    .StartAsync("Checking so the name is not taken...", _ => reposClient.CheckNameTakenV1Async(new() { Name = name }, cancellationToken));

                nameTaken = nameTakenResult.IsTaken;
            }
        }

        return name;
    }


    public class Settings : CommandSettings
    {
        [CommandOption("--repo-id")]
        public Guid? RepoId { get; init; }

        [CommandOption("--set-name")]
        public string? SetName { get; init; }
    }
}
