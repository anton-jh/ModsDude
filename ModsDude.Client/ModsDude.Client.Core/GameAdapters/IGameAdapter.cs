using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

public interface IGameAdapter
{
    GameAdapterId Id { get; }
    string DisplayName { get; }
    string Description { get; }

    DynamicForm GetBaseSettingsTemplate();
    IBaseGameAdapter WithBaseSettings(string serializedBaseSettings);
    IBaseGameAdapter WithBaseSettings(DynamicForm baseSettings);
}

public interface IBaseGameAdapter : IGameAdapter
{
    DynamicForm BaseSettings { get; }
    bool CanSupportMods { get; }
    bool CanSupportSavegames { get; }

    /// <summary>
    /// The identity of the game these base settings configure the adapter for. An adapter serving
    /// one game says nothing and gets its id alone; one serving several overrides this from a base
    /// settings field, which must not be marked [CanBeModified] - see
    /// docs/04-game-adapters.md#instance-scope.
    /// </summary>
    /// <remarks>
    /// <see cref="GameAdapterId.Id"/> without the compatibility version, deliberately: a repo on
    /// '@2' still matches instances created under '@1', which is what compatibility versions exist
    /// for.
    /// </remarks>
    InstanceScope Scope => new(Id.Id);

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
    IInstanceModAdapter WithInstanceSettings(string serializedInstanceSettings);
    IInstanceModAdapter WithInstanceSettings(DynamicForm instanceSettings);
}

public interface IInstanceModAdapter : IBaseModAdapter
{
    /// <summary>
    /// The mod folder this instance owns. No two instances may own the same one, whatever their
    /// scopes - scoping instances to a game rather than an adapter is what makes that possible.
    /// </summary>
    string ModFolder { get; }

    Task<IEnumerable<LocalMod>> GetInstalledMods(CancellationToken cancellationToken);
}

public interface IBaseSavegameAdapter
{
    IInstanceSavegameAdapter WithInstanceSettings(string serializedInstanceSettings);
    IInstanceSavegameAdapter WithInstanceSettings(DynamicForm instanceSettings);
}

public interface IInstanceSavegameAdapter : IBaseSavegameAdapter
{

}
