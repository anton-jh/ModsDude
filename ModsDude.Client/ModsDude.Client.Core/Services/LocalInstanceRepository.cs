using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Sync;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

public class LocalInstanceRepository : IInstanceModFolders, IDriftCandidateSource
{
    private readonly StateStore _store;
    private readonly SyncManifestStore _manifestStore;
    private readonly LocalState _state;


    public LocalInstanceRepository(StateStore store, SyncManifestStore manifestStore)
    {
        _store = store;
        _manifestStore = manifestStore;
        _state = store.Get();

        Instances = new(_state.Instances.Values.Select(x => new LocalInstance(x)));
    }


    /// <summary>Every instance on this machine, across all scopes.</summary>
    public ObservableCollection<LocalInstance> Instances { get; }

    /// <summary>
    /// Raised after any change to an instance that is not an add or a remove.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The half <see cref="Instances"/> cannot report.</b> Adding and deleting raise
    /// <c>CollectionChanged</c>, so a listener already hears about those; repointing an instance at a
    /// different mod folder, or at a different profile, changes nothing about the collection and used
    /// to be silent. Both of those change whether the folder matches what was applied to it, which is
    /// the entire question the drift check answers - see
    /// docs/07-mod-sync-design.md#it-has-to-be-unmissable-everywhere.
    /// </para>
    /// <para>
    /// Raised after the state has been written, so a listener that reads the instance back gets what
    /// was saved rather than what is about to be.
    /// </para>
    /// </remarks>
    public event EventHandler? InstanceChanged;


    public IEnumerable<LocalInstance> GetByScope(InstanceScope scope)
    {
        return Instances.Where(x => x.Scope == scope);
    }

    /// <summary>
    /// The folders sync's store eviction has to know about, across every scope: an instance on a
    /// disk this store serves is relying on entries the sweep would otherwise drop.
    /// </summary>
    public IReadOnlyList<InstanceModFolder> GetAll()
    {
        return [.. Instances
            .Where(x => x.ModFolder is not null)
            .Select(x => new InstanceModFolder(x.Id, x.ModFolder!))];
    }

    /// <summary>
    /// Every instance the drift check has to look at, across every scope. An instance whose scope no
    /// repo on this machine serves still owns its folder and still has a standing intent, and the
    /// check runs off both without hydrating an adapter.
    /// </summary>
    public IReadOnlyList<DriftCandidate> GetDriftCandidates()
    {
        return [.. Instances.Select(x => new DriftCandidate(x.Id, x.Name, x.ModFolder, x.ActiveProfile))];
    }

    /// <summary>
    /// The instances a save on this profile re-applies to. See <see cref="ProfileApplyTargets"/> for
    /// why this is derived rather than chosen.
    /// </summary>
    public IReadOnlyList<LocalInstance> GetInstancesUsing(ActiveProfile profile)
    {
        return ProfileApplyTargets.Derive(Instances, profile);
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

        // The mod folder may have moved, which makes every answer about the old one meaningless.
        InstanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetActiveProfile(LocalInstance instance, ActiveProfile? activeProfile)
    {
        instance.SetActiveProfile(activeProfile);
        _store.Save();

        // A folder that was in sync with one profile is drifted from another the moment it is pointed
        // at it, and nothing about the collection changed to say so.
        InstanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stops every instance tracking a profile that no longer exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called when a profile is permanently deleted from a repo's archive. An <em>archived</em>
    /// profile is deliberately still tracked - it exists, and everything pointing at it goes on
    /// pointing at it - so this is the one event that lets go, and it is the deletion rather than
    /// the archiving.
    /// </para>
    /// <para>
    /// Local state, which is why it lives here: the server has no idea which machines were pointed
    /// at the profile, and an instance whose active profile is a dangling id would report drift
    /// against a mod list nobody can read.
    /// </para>
    /// </remarks>
    public void StopTracking(Guid profileId)
    {
        var affected = Instances
            .Where(x => x.ActiveProfile?.ProfileId == profileId)
            .ToList();

        if (affected.Count == 0)
        {
            return;
        }

        foreach (var instance in affected)
        {
            instance.SetActiveProfile(null);
        }

        // One save for the batch: they were all made unusable by one event.
        _store.Save();

        InstanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(LocalInstance instance)
    {
        _state.Instances.Remove(instance.Id);
        Instances.Remove(instance);
        _store.Save();

        // Nothing reads a manifest for an instance that no longer exists, and leaving one behind
        // would keep a few hundred kilobytes per disconnected game folder forever.
        _manifestStore.Delete(instance.Id);
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
