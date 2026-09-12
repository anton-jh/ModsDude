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
public class Game
    : INotifyPropertyChanged
{
    internal Game(PersistedGame persistedModel)
    {
        PersistedModel = persistedModel;
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id => PersistedModel.Id;
    public GameIdentity Scope => PersistedModel.Scope;
    public GameAdapterId GameAdapterId => PersistedModel.GameAdapterId;
    public string Name => PersistedModel.Name;
    public string SerializedLocalSettings => PersistedModel.AdapterLocalSettings;
    public string? ModFolder => PersistedModel.ModFolder;
    public ActiveProfile? ActiveProfile => PersistedModel.ActiveProfile;

    internal PersistedGame PersistedModel { get; }


    public DynamicForm GetLocalSettings(IBaseGameAdapter baseAdapter)
    {
        return baseAdapter.DeserializeLocalSettings(PersistedModel.AdapterLocalSettings);
    }

    public ILocalGameAdapter GetAdapter(IBaseGameAdapter baseAdapter)
    {
        return baseAdapter.WithLocalSettings(PersistedModel.AdapterLocalSettings);
    }


    internal void Update(string name, DynamicForm localSettings, string? modFolder)
    {
        PersistedModel.Name = name;
        PersistedModel.AdapterLocalSettings = localSettings.Serialize();
        PersistedModel.ModFolder = modFolder;

        PropertyChanged?.Invoke(this, new(nameof(Name)));
        PropertyChanged?.Invoke(this, new(nameof(SerializedLocalSettings)));
        PropertyChanged?.Invoke(this, new(nameof(ModFolder)));
    }

    internal void SetActiveProfile(ActiveProfile? activeProfile)
    {
        PersistedModel.ActiveProfile = activeProfile;

        PropertyChanged?.Invoke(this, new(nameof(ActiveProfile)));
    }
}
