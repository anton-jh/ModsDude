using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>
/// Where the profile mod editor's versions come from: the repo, the game's remote sources, the game's
/// folders, Downloads, folders added for the session, and other profiles - as a row of chips.
/// </summary>
/// <remarks>
/// Which sources are loaded is part of the editor's undo history, so it is read from the page and
/// changed through it. Whether a loaded source is switched on is not, and lives here. Remote sources
/// are always loaded and start switched on.
/// </remarks>
public sealed partial class EditorSourcesViewModel : ObservableObject
{
    private readonly ModCatalog _catalog;
    private readonly OtherProfilesReader _otherProfiles;
    private readonly IFilePickerService _filePickerService;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;
    private readonly Func<ProfileEditorSnapshot> _current;
    private readonly Func<ProfileEditorSnapshot, string, Task> _commit;
    private readonly Func<Task> _recompose;
    private readonly CancellationToken _cancellation;

    private readonly HashSet<ModSourceId> _standby = [];
    private readonly Dictionary<ModSourceId, ProfileModSource> _profiles = [];
    private readonly IReadOnlyList<RemoteSourceLookup> _remote;

    private ProfileEditorCatalog? _view;


    public EditorSourcesViewModel(
        ModCatalog catalog,
        IReadOnlyList<IRemoteModSource> remoteSources,
        OtherProfilesReader otherProfiles,
        IFilePickerService filePickerService,
        IModalService modalService,
        IErrorReporter errorReporter,
        Func<ProfileEditorSnapshot> current,
        Func<ProfileEditorSnapshot, string, Task> commit,
        Func<Task> recompose,
        CancellationToken cancellation)
    {
        _catalog = catalog;
        _otherProfiles = otherProfiles;
        _filePickerService = filePickerService;
        _modalService = modalService;
        _errorReporter = errorReporter;
        _current = current;
        _commit = commit;
        _recompose = recompose;
        _cancellation = cancellation;

        _remote = [.. remoteSources.Select(x => new RemoteSourceLookup(x, errorReporter, cancellation))];

        foreach (var lookup in _remote)
        {
            lookup.Changed += () => RemoteChanged?.Invoke();
        }
    }


    /// <summary>
    /// Raised on the UI thread when a remote source answers or is switched on or off. Nothing on disk
    /// or in the catalog changed, so the page only places the answers again.
    /// </summary>
    public event Action? RemoteChanged;


    public ObservableCollection<ModSourceViewModel> Chips { get; } = [];

    /// <summary>Whether the repo's registered versions are among what the left list offers.</summary>
    public bool IncludeRegistered { get; private set; } = true;

    /// <summary>Whether anything at all is being offered. With nothing, <em>Not in the sources</em> means nothing.</summary>
    [ObservableProperty]
    private bool _hasEnabledSources = true;

    private bool HasEnabledFolders => _catalog.GetSources().Any(x => IsEnabled(x.Id));

    public IReadOnlyList<ProfileModSource> EnabledProfiles => [.. _profiles.Values.Where(x => IsEnabled(x.Source.Id))];

    /// <summary>What every remote source has answered, in the adapter's order.</summary>
    public IReadOnlyList<RemoteSourceAnswers> RemoteAnswers()
        => [.. _remote.Select(x => x.Answers(_standby.Contains(x.Id) is false))];

    private bool HasEnabledRemote => _remote.Any(x => _standby.Contains(x.Id) is false);

    private ImmutableHashSet<ModSourceId> Loaded => _current().Loaded;


    /// <summary>Tells the catalog which of its sources are enabled, on standby or unloaded.</summary>
    public void ApplyToCatalog()
    {
        foreach (var source in _catalog.GetSources())
        {
            _catalog.SetState(source.Id, StateOf(source.Id));
        }

        HasEnabledSources = IncludeRegistered || HasEnabledRemote || HasEnabledFolders || _profiles.Keys.Any(IsEnabled);
    }

    /// <summary>Asks every remote source about whichever of <paramref name="mods"/> it has not been asked about.</summary>
    public void LookUpRemote(IReadOnlyCollection<ModKey> mods)
    {
        foreach (var lookup in _remote)
        {
            lookup.LookUp(mods);
        }
    }

    public void RebuildChips(ProfileEditorCatalog view)
    {
        _view = view;

        Chips.Clear();

        Chips.Add(new ModSourceViewModel(
            new ModSourceStatus(
                new ModSource(ModSourceId.Repo, "This repo", "Everything this repo has registered", ModSourceKind.Repo),
                IncludeRegistered,
                view.Visible.Count(x => x.IsOnServer),
                null),
            OnChipToggled));

        foreach (var lookup in _remote)
        {
            var enabled = _standby.Contains(lookup.Id) is false;

            Chips.Add(new ModSourceViewModel(
                new ModSourceStatus(
                    lookup.AsSource,
                    enabled,
                    view.Visible.Count(x => x.Download?.SourceId == lookup.Id),
                    lookup.Error),
                OnChipToggled,
                hasCount: enabled,
                detail: lookup.Status));
        }

        var scanned = view.Snapshot.Sources.ToDictionary(x => x.Source.Id);

        foreach (var source in _catalog.GetSources())
        {
            var loaded = Loaded.Contains(source.Id);

            if (source.Kind is ModSourceKind.AdHoc && loaded is false)
            {
                continue;
            }

            var status = scanned.GetValueOrDefault(source.Id);

            Chips.Add(new ModSourceViewModel(
                new ModSourceStatus(source, IsEnabled(source.Id), status?.ModCount ?? 0, status?.Error),
                OnChipToggled,
                hasCount: IsEnabled(source.Id),
                canUnload: loaded));
        }

        foreach (var profile in _profiles.Values.Where(x => Loaded.Contains(x.Source.Id)))
        {
            Chips.Add(new ModSourceViewModel(
                new ModSourceStatus(profile.Source, IsEnabled(profile.Source.Id), profile.Pins.Count, null),
                OnChipToggled,
                canUnload: true));
        }
    }

    /// <summary>
    /// Makes one source the only one enabled, for an editor opened at that folder by the drift notice.
    /// Answers with what should then be loaded.
    /// </summary>
    public ImmutableHashSet<ModSourceId> FocusOn(ModSourceId id)
    {
        foreach (var other in Loaded.Where(x => x != id))
        {
            _standby.Add(other);
        }

        _standby.Remove(id);
        IncludeRegistered = false;

        if (Loaded.Contains(id) is false)
        {
            _catalog.Rescan(id);
        }

        return Loaded.Add(id);
    }

    [RelayCommand]
    private Task RescanAll()
    {
        _catalog.RescanAll();

        foreach (var lookup in _remote)
        {
            lookup.CheckAgain();
        }

        return _recompose();
    }

    /// <summary>Adds a folder for this session only. Nothing about it is written to disk.</summary>
    [RelayCommand]
    private Task AddFolder()
    {
        if (_filePickerService.PickFolder(null) is not string path)
        {
            return Task.CompletedTask;
        }

        var source = _catalog.AddAdHocSource(path);

        _catalog.Rescan(source.Id);

        return LoadAsync(source.Id, source.Name);
    }

    /// <summary>
    /// Reads another profile in this repo as a source. With the repo switched off, the left list is then
    /// exactly what that profile has and this one does not.
    /// </summary>
    [RelayCommand]
    private async Task AddProfile()
    {
        try
        {
            var others = (await _otherProfiles.ListAsync(_cancellation))
                .Where(x => Loaded.Contains(ModSourceId.ForProfile(x.Id)) is false)
                .ToList();

            var modal = new PickProfileSourceModalViewModel(others);

            await _modalService.Show(modal);

            if (modal.Result is not Profile picked)
            {
                return;
            }

            var id = ModSourceId.ForProfile(picked.Id);

            _profiles[id] = new ProfileModSource(
                picked.Id,
                new ModSource(id, picked.Name, "Another profile in this repo.", ModSourceKind.Profile),
                await _otherProfiles.ReadPinsAsync(picked.Id, _cancellation));

            await LoadAsync(id, picked.Name);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "reading another profile's mod list as a source");
        }
    }

    /// <summary>
    /// Takes away everything a source supplied, including pins whose file only it holds. Undoable, so it
    /// only asks when the draft would lose something.
    /// </summary>
    [RelayCommand]
    private async Task Unload(ModSourceViewModel? chip)
    {
        if (chip is null || Loaded.Contains(chip.Source.Id) is false)
        {
            return;
        }

        var snapshot = _current();
        var dependent = _view?.PinsOnlyIn(snapshot.Draft, chip.Source.Id) ?? [];

        if (dependent.Count > 0)
        {
            var confirmation = new ConfirmationModalViewModel(
                $"Unload {chip.Name}?",
                $"{ProfileModsEditorSummary.Mods(dependent.Count)} in your draft come only from {chip.Name} "
                    + "and will be taken out with it.",
                IconKind.Question,
                "Unload",
                "Keep it");

            await _modalService.Show(confirmation);

            if (confirmation.Result is false)
            {
                return;
            }
        }

        await _commit(
            new ProfileEditorSnapshot(snapshot.Draft.Remove(dependent), snapshot.Loaded.Remove(chip.Source.Id)),
            $"Unloaded {chip.Name}");
    }


    private void OnChipToggled(ModSourceViewModel chip, bool enabled)
    {
        var id = chip.Source.Id;

        if (chip.IsRepo)
        {
            IncludeRegistered = enabled;
            _ = _recompose();
        }
        else if (chip.IsRemote)
        {
            if (enabled)
            {
                _standby.Remove(id);
            }
            else
            {
                _standby.Add(id);
            }

            ApplyToCatalog();
            RemoteChanged?.Invoke();
        }
        else if (Loaded.Contains(id))
        {
            if (enabled)
            {
                _standby.Remove(id);
            }
            else
            {
                _standby.Add(id);
            }

            _ = _recompose();
        }
        else if (enabled)
        {
            _catalog.Rescan(id);
            _ = LoadAsync(id, chip.Name);
        }
    }

    private Task LoadAsync(ModSourceId id, string name)
    {
        _standby.Remove(id);

        return Loaded.Contains(id)
            ? _recompose()
            : _commit(_current() with { Loaded = Loaded.Add(id) }, $"Loaded {name}");
    }

    private ModSourceState StateOf(ModSourceId id)
        => Loaded.Contains(id) is false
            ? ModSourceState.Unloaded
            : _standby.Contains(id) ? ModSourceState.Standby : ModSourceState.Enabled;

    private bool IsEnabled(ModSourceId id) => StateOf(id) is ModSourceState.Enabled;
}
