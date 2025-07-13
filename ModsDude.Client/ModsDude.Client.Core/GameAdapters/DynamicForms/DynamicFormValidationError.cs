using System.Reflection;

namespace ModsDude.Client.Core.GameAdapters.DynamicForms;

public interface IDynamicFormValidationError
{
    string Message { get; }
    PropertyInfo[] Properties { get; }
}

public record DynamicFormValidationError<TForm> : IDynamicFormValidationError
    where TForm : IDynamicForm
{
    public DynamicFormValidationError(string message, params PropertyInfo[] properties)
    {
        if (properties.Length == 0)
        {
            throw new ArgumentException("Validation error must be associated with at least one property!", nameof(properties));
        }

        foreach (var property in properties)
        {
            if (property.DeclaringType != typeof(TForm))
            {
                throw new ArgumentException($"Property value associated with a Validation error must belong to {typeof(TForm).Name}");
            }
        }

        Message = message;
        Properties = properties;
    }

    public DynamicFormValidationError(string message, params string[] propertyNames)
        : this(message, propertyNames.Select(GetPropertyInfo).ToArray())
    { }

    public string Message { get; }
    public PropertyInfo[] Properties { get; }


    private static PropertyInfo GetPropertyInfo(string propertyName)
    {
        return typeof(TForm).GetProperty(propertyName)
            ?? throw new ArgumentException($"Property '{propertyName}' does not exist on '{typeof(TForm).Name}'.");
    }
};
