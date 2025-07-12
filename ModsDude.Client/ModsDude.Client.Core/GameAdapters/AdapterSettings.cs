namespace ModsDude.Client.Core.GameAdapters;
public interface IAdapterSettings
{
    IAdapterSettingsValidationError[] Validate();
}


public abstract class AdapterSettings<T> : IAdapterSettings
    where T : AdapterSettings<T>
{
    IAdapterSettingsValidationError[] IAdapterSettings.Validate()
    {
        return Validate().ToArray();
    }

    protected virtual IEnumerable<AdapterSettingsValidationError<T>> Validate()
    {
        return [];
    }
}
