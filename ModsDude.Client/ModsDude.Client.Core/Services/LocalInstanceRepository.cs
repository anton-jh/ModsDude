using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

public class LocalInstanceRepository
{
    private readonly StateStore _store;
    private readonly LocalState _state;


    public LocalInstanceRepository(StateStore store)
    {
        _store = store;
        _state = store.Get();

        Instances = new(_state.Instances.Values.Select(x => new LocalInstance(x)));
    }


    /// <summary>Every instance on this machine, across all scopes.</summary>
    public ObservableCollection<LocalInstance> Instances { get; }


    public IEnumerable<LocalInstance> GetByScope(InstanceScope scope)
    {
        return Instances.Where(x => x.Scope == scope);
    }

    public LocalInstance Create(IBaseGameAdapter baseAdapter, string name, DynamicForm instanceSettings)
    {
        var modFolder = GetModFolder(baseAdapter, instanceSettings);

        EnsureFolderIsUnclaimed(modFolder, null);

        var persistedModel = new PersistedLocalInstance()
        {
            Id = Guid.NewGuid(),
            Scope = baseAdapter.Scope,
            GameAdapterId = baseAdapter.Id,
            Name = name,
            AdapterInstanceSettings = instanceSettings.Serialize(),
            ModFolder = modFolder
        };

        var instance = new LocalInstance(persistedModel);

        _state.Instances[persistedModel.Id] = persistedModel;
        Instances.Add(instance);
        _store.Save();

        return instance;
    }

    public void Update(LocalInstance instance, IBaseGameAdapter baseAdapter, string name, DynamicForm instanceSettings)
    {
        var modFolder = GetModFolder(baseAdapter, instanceSettings);

        EnsureFolderIsUnclaimed(modFolder, instance.Id);

        instance.Update(name, instanceSettings, modFolder);
        _store.Save();
    }

    public void SetActiveProfile(LocalInstance instance, ActiveProfile? activeProfile)
    {
        instance.SetActiveProfile(activeProfile);
        _store.Save();
    }

    public void Delete(LocalInstance instance)
    {
        _state.Instances.Remove(instance.Id);
        Instances.Remove(instance);
        _store.Save();
    }

    /// <summary>
    /// The instance already claiming the folder these settings point at, if any. Checked across
    /// every scope: two scopes can name the same folder, and only one instance can own it.
    /// </summary>
    public LocalInstance? FindFolderConflict(IBaseGameAdapter baseAdapter, DynamicForm instanceSettings, Guid? ignoredInstanceId = null)
    {
        return FindFolderConflict(GetModFolder(baseAdapter, instanceSettings), ignoredInstanceId);
    }

    /// <summary>The mod folder the adapter says an instance with these settings would own.</summary>
    public static string? GetModFolder(IBaseGameAdapter baseAdapter, DynamicForm instanceSettings)
    {
        return baseAdapter
            .WithInstanceSettings(instanceSettings)
            .GetInstanceCapabilityAdapterFactory<IInstanceModAdapter>()
            ?.Invoke()
            .ModFolder;
    }


    private LocalInstance? FindFolderConflict(string? modFolder, Guid? ignoredInstanceId)
    {
        if (modFolder is null)
        {
            return null;
        }

        return Instances.FirstOrDefault(x =>
            x.Id != ignoredInstanceId &&
            FileSystemHelper.ArePathsEqual(x.ModFolder, modFolder));
    }

    private void EnsureFolderIsUnclaimed(string? modFolder, Guid? ignoredInstanceId)
    {
        if (FindFolderConflict(modFolder, ignoredInstanceId) is LocalInstance owner)
        {
            throw new UserFriendlyException(
                $"'{owner.Name}' already uses that folder",
                $"A folder may only be claimed by one instance: '{modFolder}' belongs to '{owner.Name}'.");
        }
    }
}
