using ModsDude.Client.Cli.Commands.Abstractions;
using ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
using ModsDude.Client.Cli.DynamicForms;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class CreateRepoCommand(
    IAnsiConsole ansiConsole,
    IReposClient reposClient,
    IGameAdapterIndex gameAdapterIndex,
    FormPrompter formPrompter,
    RepoNameCollector repoNameCollector)
    : AsyncCommandBase<CreateRepoCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, bool runFromMenu, CancellationToken cancellationToken)
    {
        var name = await repoNameCollector.Collect(settings.Name, runFromMenu, cancellationToken);
        var gameAdapter = await CollectGameAdapter(settings, runFromMenu, cancellationToken);
        var adapterBaseSettings = await CollectAdapterBaseSettings(gameAdapter, runFromMenu, cancellationToken);

        await _ansiConsole.Status()
            .StartAsync("Creating repo...", async ctx =>
            {
                await reposClient.CreateRepoV1Async(new()
                {
                    Name = name,
                    AdapterId = gameAdapter.Descriptor.Id.ToString(),
                    AdapterConfiguration = adapterBaseSettings.Serialize(),
                }, cancellationToken);
            });

        _ansiConsole.If(runFromMenu)?.Clear();

        _ansiConsole.MarkupLine("Repo successfully created.");
        _ansiConsole.WriteLine();
        _ansiConsole.If(runFromMenu)?.PressAnyKeyToDismiss();
    }

    private async Task<IGameAdapter> CollectGameAdapter(Settings settings, bool runFromMenu, CancellationToken cancellationToken)
    {
        if (settings.Adapter is not null)
        {
            var adapter = GameAdapterId.TryParse(settings.Adapter, out var parsedAdapterId)
                ? gameAdapterIndex.GetById(parsedAdapterId)
                : gameAdapterIndex.GetLatestByPartialId(settings.Adapter);

            if (adapter is not null)
            {
                return adapter;
            }
        }

        _ansiConsole.If(runFromMenu)?.Clear();
        var prompt = new SelectionPrompt<IGameAdapter>()
            .Title("[yellow]Select the game adapter to use:[/]")
            .UseConverter(x => $"[yellow]{x.Descriptor.DisplayName}[/]: {x.Descriptor.Description}")
            .AddChoices(gameAdapterIndex.GetAllLatest());

        return await _ansiConsole.PromptAsync(prompt, cancellationToken);
    }

    private async Task<IDynamicForm> CollectAdapterBaseSettings(IGameAdapter gameAdapter, bool runFromMenu, CancellationToken cancellationToken)
    {
        var adapterBaseSettings = gameAdapter.GetBaseSettingsTemplate();

        await formPrompter.Prompt(
            form: adapterBaseSettings,
            title: "[blue bold]Configure base settings for the game adapter[/]",
            onlyModify: false,
            runFromMenu: runFromMenu,
            cancellationToken: cancellationToken);

        return adapterBaseSettings;
    }


    public class Settings : CommandSettings
    {
        [CommandOption("-n|--name")]
        public string? Name { get; init; }

        [CommandOption("--adapter")]
        public string? Adapter { get; init; }
    }
}
