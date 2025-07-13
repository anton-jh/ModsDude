namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

public interface IDynamicForm
{
    IDynamicFormValidationError[] Validate();
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
}
