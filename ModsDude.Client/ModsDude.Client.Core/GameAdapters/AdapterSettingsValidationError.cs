using System.Reflection;

namespace ModsDude.Client.Core.GameAdapters;

public interface IAdapterSettingsValidationError
{
    string Message { get; }
    PropertyInfo[] Properties { get; }
}

public record AdapterSettingsValidationError<TSettings> : IAdapterSettingsValidationError
{
    public AdapterSettingsValidationError(string message, params PropertyInfo[] properties)
    {
        if (properties.Length == 0)
        {
            throw new ArgumentException("Validation error must be associated with at least one property!", nameof(properties));
        }

        foreach (var property in properties)
        {
            if (property.DeclaringType != typeof(TSettings))
            {
                throw new ArgumentException($"Property value associated with a Validation error must belong to {typeof(TSettings).Name}");
            }
        }

        Message = message;
        Properties = properties;
    }

    public AdapterSettingsValidationError(string message, params string[] propertyNames)
        : this(message, propertyNames.Select(x => GetPropertyInfo(x)).ToArray())
    { }

    public string Message { get; }
    public PropertyInfo[] Properties { get; }


    private static PropertyInfo GetPropertyInfo(string propertyName)
    {
        return typeof(TSettings).GetProperty(propertyName)
            ?? throw new ArgumentException($"Property '{propertyName}' does not exist on '{typeof(TSettings).Name}'.");
    }
};
