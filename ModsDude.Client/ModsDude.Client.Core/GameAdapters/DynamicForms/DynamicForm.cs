using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

public abstract class DynamicForm
{
    public abstract DynamicFormValidationError[] Validate();
    public abstract void EnsureValid();
    public abstract DynamicForm Copy();

    public virtual string Serialize()
    {
        return JsonSerializer.Serialize(this, GetType());
    }


    public static string GetFieldTitle(PropertyInfo property)
    {
        return property.GetCustomAttribute<TitleAttribute>()?.Text ?? property.Name;
    }
}


public abstract class DynamicForm<T> : DynamicForm
    where T : DynamicForm<T>, new()
{
    public override DynamicFormValidationError[] Validate()
    {
        return PerformValidation().ToArray();
    }

    public override void EnsureValid()
    {
        var errors = PerformValidation();

        if (errors.Any())
        {
            throw new ArgumentException($"Invalid base settings:\n" +
                $"{string.Join('\n', errors.Select(x => x.Message))}");
        }
    }

    protected virtual IEnumerable<DynamicFormValidationError<T>> PerformValidation()
    {
        return [];
    }

    public override DynamicForm Copy()
    {
        var props = typeof(T).GetProperties();
        var copy = new T();

        foreach (var prop in props)
        {
            var value = prop.GetValue(this);
            prop.SetValue(copy, value);
        }

        return copy;
    }
}
