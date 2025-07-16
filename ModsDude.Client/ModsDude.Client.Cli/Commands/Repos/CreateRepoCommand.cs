using ModsDude.Client.Cli.Commands.Shared;
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
                    AdapterConfiguration = adapterBaseSettings.Serialize(),
                }, cancellationToken);
            });

        _ansiConsole.Clear();

        _ansiConsole.MarkupLine("Repo successfully created.");
        _ansiConsole.WriteLine();
        _ansiConsole.PressAnyKeyToDismiss();
    }


    private async Task<string> CollectName(Settings settings, CancellationToken cancellationToken)
    {
        var nameTaken = false;
        var name = settings.Name;

        while (nameTaken || string.IsNullOrWhiteSpace(name))
        {
            _ansiConsole.Clear();
            if (nameTaken)
            {
                _ansiConsole.MarkupLineInterpolated($"[red]Name '{name}' taken.[/]");
            }
            var prompt = new TextPrompt<string>("[yellow]Give the repo a friendly name:[/]");

            name = await _ansiConsole.PromptAsync(prompt, cancellationToken);
            name = name.Trim();

            var nameTakenResult = await _ansiConsole.Status()
                    .StartAsync("Checking so the name is not taken...", _ => reposClient.CheckNameTakenV1Async(new() { Name = name }, cancellationToken));

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

        await formPrompter.Prompt(
            form: adapterBaseSettings,
            title: "[blue bold]Configure base settings for the game adapter[/]",
            onlyModify: false,
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
