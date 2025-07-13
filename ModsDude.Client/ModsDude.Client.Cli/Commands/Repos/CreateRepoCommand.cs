using ModsDude.Client.Cli.Commands.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Reflection;
using System.Text.Json;

namespace ModsDude.Client.Cli.Commands.Repos;
internal class CreateRepoCommand(
    IAnsiConsole ansiConsole,
    IReposClient reposClient,
    IGameAdapterIndex gameAdapterIndex)
    : AsyncCommandBase<CreateRepoCommand.Settings>(ansiConsole)
{
    public override async Task ExecuteAsync(Settings settings, CancellationToken cancellationToken)
    {
        var name = await CollectName(settings, cancellationToken);
        var gameAdapter = await CollectGameAdapter(settings, cancellationToken);
        var adapterBaseSettings = await CollectAdapterBaseSettings(settings, gameAdapter, cancellationToken);

        await reposClient.CreateRepoV1Async(new()
        {
            Name = name,
            AdapterId = gameAdapter.Descriptor.Id.ToString(),
            AdapterConfiguration = JsonSerializer.Serialize(adapterBaseSettings),
        }, cancellationToken);
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

    private async Task<IAdapterSettings> CollectAdapterBaseSettings(Settings settings, IGameAdapter gameAdapter, CancellationToken cancellationToken)
    {
        // todo how to collect this from command line parameters? --base-settings-[propertyName] maybe? can i do that with Spectre?

        var adapterBaseSettings = gameAdapter.GetBaseSettingsTemplate();

        IEnumerable<IAdapterSettingsValidationError> validationErrors = [];

        do
        {
            await SetBaseSettings(adapterBaseSettings, validationErrors, cancellationToken);
            validationErrors = adapterBaseSettings.Validate();

        } while (validationErrors.Any());

        return adapterBaseSettings;
    }

    private async Task SetBaseSettings(IAdapterSettings settings, IEnumerable<IAdapterSettingsValidationError> validationErrors, CancellationToken cancellationToken)
    {
        var isFirst = true;
        var properties = settings.GetType().GetProperties().Where(x => x.CanWrite);

        if (validationErrors.Any())
        {
            properties = validationErrors.SelectMany(x => x.Properties).Distinct();
        }

        _ansiConsole.Clear();
        _ansiConsole.MarkupLine("[blue bold]Configure base settings for the game[/]");
        _ansiConsole.WriteLine();

        foreach (var property in properties)
        {
            if (property.PropertyType == typeof(string))
            {
                var title = property.GetCustomAttribute<TitleAttribute>()?.Text ?? property.Name;
                var defaultValue = (string?)property.GetValue(settings);
                var isRequired = property.GetCustomAttribute<RequiredAttribute>() is not null;
                var propertyValidationErrors = validationErrors.Where(x => x.Properties.Contains(property));

                if (validationErrors.Any() && !isFirst)
                {
                    _ansiConsole.WriteLine();
                }
                isFirst = false;

                foreach (var validationError in propertyValidationErrors)
                {
                    var style = validationError.Properties.Length > 1
                        ? new Style(Color.Orange1)
                        : new Style(Color.Red);

                    _ansiConsole.WriteLine(validationError.Message, style);
                }

                var prompt = new TextPrompt<string?>(isRequired
                    ? $"[yellow]{title}[/]"
                    : $"[yellow]{title} [[optional]][/]")
                {
                    AllowEmpty = !isRequired
                }; // todo colon if no default value

                if (string.IsNullOrWhiteSpace(defaultValue) == false)
                {
                    prompt.DefaultValue(defaultValue);
                }

                var newValue = await _ansiConsole.PromptAsync(prompt, cancellationToken);
                if (string.IsNullOrWhiteSpace(newValue))
                {
                    newValue = null;
                }

                property.SetValue(settings, newValue);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Invalid settings property type '{property.PropertyType}' ({property.DeclaringType?.Name}.{property.Name})");
            }
        }
    }


    public class Settings : CommandSettings
    {
        [CommandOption("-n|--name")]
        public string? Name { get; init; }

        [CommandOption("--adapter")]
        public string? Adapter { get; init; }
    }
}
