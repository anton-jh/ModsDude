using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

public interface IDynamicForm
{
    IDynamicFormValidationError[] Validate();
    string Serialize();
}


public abstract class DynamicForm<T> : IDynamicForm
    where T : DynamicForm<T>
{
    IDynamicFormValidationError[] IDynamicForm.Validate()
    {
        return Validate().ToArray();
    }

    protected virtual IEnumerable<DynamicFormValidationError<T>> Validate()
    {
        return [];
    }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, GetType());
    }
}
