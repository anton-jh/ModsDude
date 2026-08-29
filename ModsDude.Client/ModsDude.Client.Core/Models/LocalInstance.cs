using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Persistence;
using System.ComponentModel;

namespace ModsDude.Client.Core.Models;

/// <summary>
/// One mod folder on this machine: a sync target. Scoped to a game rather than to a repo, so the
/// settings it carries are hydrated by whichever repo offers it - they are the same settings under
/// all of them.
/// </summary>
public class LocalInstance
    : INotifyPropertyChanged
{
    internal LocalInstance(PersistedLocalInstance persistedModel)
    {
        PersistedModel = persistedModel;
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id => PersistedModel.Id;
    public InstanceScope Scope => PersistedModel.Scope;
    public GameAdapterId GameAdapterId => PersistedModel.GameAdapterId;
    public string Name => PersistedModel.Name;
    public string SerializedInstanceSettings => PersistedModel.AdapterInstanceSettings;
    public string? ModFolder => PersistedModel.ModFolder;
    public ActiveProfile? ActiveProfile => PersistedModel.ActiveProfile;

    internal PersistedLocalInstance PersistedModel { get; }


    public DynamicForm GetInstanceSettings(IBaseGameAdapter baseAdapter)
    {
        return baseAdapter.DeserializeInstanceSettings(PersistedModel.AdapterInstanceSettings);
    }

    public IInstanceGameAdapter GetAdapter(IBaseGameAdapter baseAdapter)
    {
        return baseAdapter.WithInstanceSettings(PersistedModel.AdapterInstanceSettings);
    }


    internal void Update(string name, DynamicForm instanceSettings, string? modFolder)
    {
        PersistedModel.Name = name;
        PersistedModel.AdapterInstanceSettings = instanceSettings.Serialize();
        PersistedModel.ModFolder = modFolder;

        PropertyChanged?.Invoke(this, new(nameof(Name)));
        PropertyChanged?.Invoke(this, new(nameof(SerializedInstanceSettings)));
        PropertyChanged?.Invoke(this, new(nameof(ModFolder)));
    }

    internal void SetActiveProfile(ActiveProfile? activeProfile)
    {
        PersistedModel.ActiveProfile = activeProfile;

        PropertyChanged?.Invoke(this, new(nameof(ActiveProfile)));
    }
}
