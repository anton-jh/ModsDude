using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

public abstract class DynamicForm
{
    public abstract DynamicFormValidationError[] Validate();

    public abstract DynamicForm Copy();

    public virtual string Serialize()
    {
        return JsonSerializer.Serialize(this, GetType());
    }
}


public abstract class DynamicForm<T> : DynamicForm
    where T : DynamicForm<T>, new()
{
    public override DynamicFormValidationError[] Validate()
    {
        return PerformValidation().ToArray();
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
