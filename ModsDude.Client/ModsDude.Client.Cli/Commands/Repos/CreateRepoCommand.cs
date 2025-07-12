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
        var name = settings.Name ?? await _ansiConsole.PromptAsync(new TextPrompt<string>("Give the repo a friendly name:"), cancellationToken);
        name = name.Trim();

        var gameAdapter = await SelectGameAdapter(cancellationToken);

        var adapterBaseSettings = gameAdapter.GetBaseSettingsTemplate();

        IEnumerable<IAdapterSettingsValidationError> validationErrors = [];

        do
        {
            await SetBaseSettings(adapterBaseSettings, validationErrors, cancellationToken);
            validationErrors = adapterBaseSettings.Validate();

        } while (validationErrors.Any());

        await reposClient.CreateRepoV1Async(new()
        {
            Name = name,
            AdapterId = gameAdapter.Descriptor.Id.ToString(),
            AdapterConfiguration = JsonSerializer.Serialize(adapterBaseSettings),
        }, cancellationToken);
    }


    private async Task<IGameAdapter> SelectGameAdapter(CancellationToken cancellationToken)
    {
        var prompt = new SelectionPrompt<IGameAdapter>()
            .Title("Select the game adapter to use:")
            .UseConverter(x => $"[yellow]{x.Descriptor.DisplayName}[/]: {x.Descriptor.Description}")
            .AddChoices(gameAdapterIndex.GetAllLatest());

        return await _ansiConsole.PromptAsync(prompt, cancellationToken);
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
    }
}
