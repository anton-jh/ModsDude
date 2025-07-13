using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.GameAdapters;

public interface IGameAdapter
{
    GameAdapterDescriptor Descriptor { get; }
    bool HasModAdapter { get; }
    bool HasSavegameAdapter { get; }
    IDynamicForm GetBaseSettingsTemplate();
    IDynamicForm GetInstanceSettingsTemplate();
}

public abstract class GameAdapterBase<TBaseSettings, TInstanceSettings> : IGameAdapter
    where TBaseSettings : IDynamicForm, new()
    where TInstanceSettings : IDynamicForm, new()
{
    public abstract GameAdapterDescriptor Descriptor { get; }

    public bool HasModAdapter => ModAdapter is not null;
    public bool HasSavegameAdapter => SavegameAdapter is not null;

    public abstract IModAdapter? ModAdapter { get; }
    public abstract ISavegameAdapter? SavegameAdapter { get; }

    public IDynamicForm GetBaseSettingsTemplate() => new TBaseSettings();
    public IDynamicForm GetInstanceSettingsTemplate() => new TInstanceSettings();
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
