using ModsDude.Client.Cli.Commands.Abstractions;
using ModsDude.Client.Cli.DynamicForms;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class CreateRepoCommand(
    IAnsiConsole ansiConsole,
    IReposClient reposClient,
    IGameAdapterIndex gameAdapterIndex,
    FormPrompter formPrompter)
    : AsyncCommandBase<CreateRepoCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var name = await CollectName(settings, cancellationToken);
        var gameAdapter = await CollectGameAdapter(settings, cancellationToken);
        var adapterBaseSettings = await CollectAdapterBaseSettings(gameAdapter, cancellationToken);

        await _ansiConsole.Status()
            .StartAsync("Creating repo...", async ctx =>
            {
                await reposClient.CreateRepoV1Async(new()
                {
                    Name = name,
                    AdapterId = gameAdapter.Descriptor.Id.ToString(),
                    AdapterConfiguration = JsonSerializer.Serialize(adapterBaseSettings),
                }, cancellationToken);

                await Task.Delay(2000);
            });

        _ansiConsole.Clear();

        _ansiConsole.MarkupLine("Repo successfully created.");
        _ansiConsole.WriteLine();
        _ansiConsole.PressAnyKeyToDismiss();
    }


    private async Task<string> CollectName(Settings settings, CancellationToken cancellationToken)
    {
        var nameTaken = false;
        string name = settings.Name ?? "";

        while (nameTaken || string.IsNullOrWhiteSpace(name))
        {
            _ansiConsole.Clear();
            if (nameTaken)
            {
                _ansiConsole.MarkupLineInterpolated($"[red]Name '{name}' taken.[/]");
            }
            name = settings.Name ?? await _ansiConsole.PromptAsync(new TextPrompt<string>("[yellow]Give the repo a friendly name:[/]"), cancellationToken);
            name = name.Trim();

            var nameTakenResult = await reposClient.CheckNameTakenV1Async(new() { Name = name }, cancellationToken);
            nameTaken = nameTakenResult.IsTaken;
        }

        return name;
    }

    private async Task<IGameAdapter> CollectGameAdapter(Settings settings, CancellationToken cancellationToken)
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

        _ansiConsole.Clear();
        var prompt = new SelectionPrompt<IGameAdapter>()
            .Title("[yellow]Select the game adapter to use:[/]")
            .UseConverter(x => $"[yellow]{x.Descriptor.DisplayName}[/]: {x.Descriptor.Description}")
            .AddChoices(gameAdapterIndex.GetAllLatest());

        return await _ansiConsole.PromptAsync(prompt, cancellationToken);
    }

    private async Task<IDynamicForm> CollectAdapterBaseSettings(IGameAdapter gameAdapter, CancellationToken cancellationToken)
    {
        var adapterBaseSettings = gameAdapter.GetBaseSettingsTemplate();
        await formPrompter.Prompt(adapterBaseSettings, "[blue bold]Configure base settings for the game[/]", cancellationToken);
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
