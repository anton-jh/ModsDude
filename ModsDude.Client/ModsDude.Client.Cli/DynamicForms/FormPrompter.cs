using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using Spectre.Console;
using System.Reflection;

namespace ModsDude.Client.Cli.DynamicForms;
internal class FormPrompter(IAnsiConsole ansiConsole)
{
    public async Task<bool> Prompt<TForm>(TForm form, string title, bool onlyModify, CancellationToken cancellationToken)
        where TForm : IDynamicForm
    {
        IEnumerable<IDynamicFormValidationError> validationErrors = [];
        var changed = false;

        do
        {
            var newChanged = await Prompt(
                form: form,
                titleMarkup: title,
                validationErrors: validationErrors,
                onlyModify: onlyModify,
                cancellationToken: cancellationToken);

            changed = changed || newChanged;

            validationErrors = form.Validate();

        } while (validationErrors.Any());

        return changed;
    }


    private async Task<bool> Prompt(
        IDynamicForm form,
        string titleMarkup,
        IEnumerable<IDynamicFormValidationError> validationErrors,
        bool onlyModify,
        CancellationToken cancellationToken)
    {
        var changed = false;
        var isFirst = true;
        var properties = form.GetType().GetProperties().Where(x => x.CanWrite);

        if (validationErrors.Any())
        {
            properties = validationErrors.SelectMany(x => x.Properties).Distinct();
        }

        if (onlyModify)
        {
            properties = properties.Where(x => x.GetCustomAttribute<CanBeModifiedAttribute>() is not null);
        }

        ansiConsole.Clear();
        ansiConsole.MarkupLine(titleMarkup);
        ansiConsole.WriteLine();

        if (!properties.Any())
        {
            ansiConsole.NothingHere();
            return false;
        }

        foreach (var property in properties)
        {
            if (property.PropertyType == typeof(string))
            {
                var title = property.GetCustomAttribute<TitleAttribute>()?.Text ?? property.Name;
                var defaultValue = (string?)property.GetValue(form);
                var isRequired = property.GetCustomAttribute<RequiredAttribute>() is not null;
                var propertyValidationErrors = validationErrors.Where(x => x.Properties.Contains(property));

                if (validationErrors.Any() && !isFirst)
                {
                    ansiConsole.WriteLine();
                }
                isFirst = false;

                foreach (var validationError in propertyValidationErrors)
                {
                    var style = validationError.Properties.Length > 1
                        ? new Style(Color.Orange1)
                        : new Style(Color.Red);

                    ansiConsole.WriteLine(validationError.Message, style);
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

                var newValue = await ansiConsole.PromptAsync(prompt, cancellationToken);
                if (string.IsNullOrWhiteSpace(newValue))
                {
                    newValue = null;
                }

                if (newValue != (string?)property.GetValue(form))
                {
                    changed = true;
                }

                property.SetValue(form, newValue);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Invalid form property type '{property.PropertyType}' ({property.DeclaringType?.Name}.{property.Name})");
            }
        }

        return changed;
    }
}
