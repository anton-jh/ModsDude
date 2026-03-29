using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters;

public interface IGameAdapter
{
    GameAdapterDescriptor Descriptor { get; }
    bool HasModAdapter { get; }
    bool HasSavegameAdapter { get; }
    DynamicForm BaseSettingsTemplate { get; }
    DynamicForm InstanceSettingsTemplate { get; }
    DynamicForm DeserializeBaseSettings(string serialized);
    DynamicForm DeserializeInstanceSettings(string serialized);
    string SerializeBaseSettings(DynamicForm settings);
    string SerializeInstanceSettings(DynamicForm settings);
}

public abstract class GameAdapterBase<TBaseSettings, TInstanceSettings> : IGameAdapter
    where TBaseSettings : DynamicForm, new()
    where TInstanceSettings : DynamicForm, new()
{
    public abstract GameAdapterDescriptor Descriptor { get; }

    public bool HasModAdapter => ModAdapter is not null;
    public bool HasSavegameAdapter => SavegameAdapter is not null;
    public DynamicForm BaseSettingsTemplate => new TBaseSettings();
    public DynamicForm InstanceSettingsTemplate => new TInstanceSettings();

    public abstract IModAdapter? ModAdapter { get; }
    public abstract ISavegameAdapter? SavegameAdapter { get; }


    public virtual DynamicForm DeserializeBaseSettings(string serialized)
    {
        return JsonSerializer.Deserialize<TBaseSettings>(serialized)
            ?? throw new ArgumentException("Cannot deserialize GameAdapter settings.");
    }

    public virtual DynamicForm DeserializeInstanceSettings(string serialized)
    {
        return JsonSerializer.Deserialize<TInstanceSettings>(serialized)
            ?? throw new ArgumentException("Cannot deserialize GameAdapter settings.");
    }

    public virtual string SerializeBaseSettings(DynamicForm settings)
    {
        if (settings is not TBaseSettings typed)
        {
            throw new InvalidOperationException($"Cannot serialize base settings: Expected '{typeof(TBaseSettings).FullName}', got '{settings.GetType().FullName}'");
        }

        return JsonSerializer.Serialize(typed);
    }

    public virtual string SerializeInstanceSettings(DynamicForm settings)
    {
        if (settings is not TInstanceSettings typed)
        {
            throw new InvalidOperationException($"Cannot serialize instance settings: Expected '{typeof(TInstanceSettings).FullName}', got '{settings.GetType().FullName}'");
        }

        return JsonSerializer.Serialize(typed);
    }
}

public interface IModAdapter
{
    // Task DoStuff(ILocalInstance instance) // pass local instance to methods that need it here instead of having yet another interface
}

public interface ISavegameAdapter
{
    // Task DoStuff(ILocalInstance instance) // pass local instance to methods that need it here instead of having yet another interface
}

public abstract class ModAdapterBase<TBaseSettings, TInstanceSettings> : IModAdapter
{

}

public abstract class SavegameAdapterBase<TBaseSettings, TInstanceSettings> : ISavegameAdapter
{

}
