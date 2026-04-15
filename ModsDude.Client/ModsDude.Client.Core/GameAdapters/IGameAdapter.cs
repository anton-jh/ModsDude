using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

public interface IGameAdapter
{
    GameAdapterId Id { get; }
    string DisplayName { get; }
    string Description { get; }
    bool CanSupportMods { get; }
    bool CanSupportSavegames { get; }

    DynamicForm GetBaseSettingsTemplate();
    IBaseGameAdapter WithBaseSettings(string serializedBaseSettings);
    IBaseGameAdapter WithBaseSettings(DynamicForm baseSettings);
}

public interface IBaseGameAdapter : IGameAdapter
{
    DynamicForm BaseSettings { get; }

    DynamicForm GetInstanceSettingsTemplate();
    DynamicForm DeserializeInstanceSettings(string serializedInstanceSettings);
    Func<T>? GetBaseCapabilityAdapterFactory<T>();
    IInstanceGameAdapter WithInstanceSettings(string serializedInstanceSettings);
    IInstanceGameAdapter WithInstanceSettings(DynamicForm instanceSettings);
}

public interface IInstanceGameAdapter : IBaseGameAdapter
{
    DynamicForm InstanceSettings { get; }

    Func<T>? GetInstanceCapabilityAdapterFactory<T>();
}

public interface IBaseModAdapter
{
    Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken);
}

public interface IInstanceModAdapter : IBaseModAdapter
{
    Task<IEnumerable<LocalMod>> GetModsFromInstalled(CancellationToken cancellationToken);
}

public interface IBaseSavegameAdapter
{

}

public interface IInstanceSavegameAdapter : IBaseSavegameAdapter
{

}
