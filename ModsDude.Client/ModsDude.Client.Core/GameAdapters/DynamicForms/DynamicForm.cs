using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

public abstract record DynamicForm
{
    public abstract DynamicFormValidationError[] Validate();

    public virtual string Serialize()
    {
        return JsonSerializer.Serialize(this, GetType());
    }
}


public abstract record DynamicForm<T> : DynamicForm
    where T : DynamicForm<T>
{
    public override DynamicFormValidationError[] Validate()
    {
        return Validate().ToArray();
    }

    protected virtual IEnumerable<DynamicFormValidationError<T>> PerformValidation()
    {
        return [];
    }
}
