using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Sidebar;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>
/// Where the profile mod editor's versions come from: the repo, the game's folders, the game's remote
/// sources, Downloads, folders added for the session, other profiles and savegames - as a row of chips.
/// </summary>
/// <remarks>
/// Which sources are loaded is part of the editor's undo history, so it is read from the page and
/// changed through it. Whether a loaded source is switched on is not, and lives here. Remote sources
/// are always loaded and start switched on.
/// </remarks>
public sealed partial class EditorSourcesViewModel : ObservableObject
{
    private readonly Repo _repo;
    private readonly ModCatalog _catalog;
    private readonly OtherProfilesReader _otherProfiles;
    private readonly ISavegameSlots _savegameSlots;
    private readonly IFilePickerService _filePickerService;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;
    private readonly Func<ProfileEditorSnapshot> _current;
    private readonly Func<ProfileEditorSnapshot, string, Task> _commit;
    private readonly Func<Task> _recompose;
    private readonly CancellationToken _cancellation;

    private readonly HashSet<ModSourceId> _standby = [];
    private readonly List<ProfileModSource> _profiles = [];
    private readonly List<SavegameModSource> _savegames = [];
    private readonly IReadOnlyList<RemoteSourceLookup> _remote;

    private ProfileEditorCatalog? _view;


    public EditorSourcesViewModel(
        Repo repo,
        ModCatalog catalog,
        IReadOnlyList<IRemoteModSource> remoteSources,
        OtherProfilesReader otherProfiles,
        ISavegameSlots savegameSlots,
        IFilePickerService filePickerService,
        IModalService modalService,
        IErrorReporter errorReporter,
        Func<ProfileEditorSnapshot> current,
        Func<ProfileEditorSnapshot, string, Task> commit,
        Func<Task> recompose,
        CancellationToken cancellation)
    {
        _repo = repo;
        _catalog = catalog;
        _otherProfiles = otherProfiles;
        _savegameSlots = savegameSlots;
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

    public IReadOnlyList<ProfileModSource> EnabledProfiles => [.. _profiles.Where(x => IsEnabled(x.Source.Id))];

    public IReadOnlyList<SavegameModSource> EnabledSavegames => [.. _savegames.Where(x => IsEnabled(x.Source.Id))];

    /// <summary>Whether any of this repo's games on this machine has savegames that list their mods.</summary>
    public bool CanAddSavegame => _repo.Games.Any(_savegameSlots.ListsMods);

    /// <summary>What every remote source has answered, in the adapter's order.</summary>
    public IReadOnlyList<RemoteSourceAnswers> RemoteAnswers()
        => [.. _remote.Select(x => x.Answers(_standby.Contains(x.Id) is false))];

    private bool HasEnabledRemote => _remote.Any(x => _standby.Contains(x.Id) is false);

    private ImmutableHashSet<ModSourceId> Loaded => _current().Loaded;

    /// <summary>The game's mod folders: what an editor opens with loaded.</summary>
    public ImmutableHashSet<ModSourceId> Targets => _catalog.GetSources()
        .Where(x => x.Kind is ModSourceKind.Game)
        .Select(x => x.Id)
        .ToImmutableHashSet();


    /// <summary>Tells the catalog which of its sources are enabled, on standby or unloaded.</summary>
    public void ApplyToCatalog()
    {
        foreach (var source in _catalog.GetSources())
        {
            _catalog.SetState(source.Id, StateOf(source.Id));
        }

        HasEnabledSources = IncludeRegistered
            || HasEnabledRemote
            || HasEnabledFolders
            || _profiles.Any(x => IsEnabled(x.Source.Id))
            || _savegames.Any(x => IsEnabled(x.Source.Id));
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
                ModSource.ThisRepo,
                IncludeRegistered,
                view.Visible.Count(x => x.IsOnServer),
                null),
            OnChipToggled));

        var scanned = view.Snapshot.Sources.ToDictionary(x => x.Source.Id);
        var folders = _catalog.GetSources();

        AddFolderChips(folders.Where(x => x.Kind is ModSourceKind.Game), scanned);

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

        AddFolderChips(folders.Where(x => x.Kind is not ModSourceKind.Game), scanned);

        foreach (var profile in _profiles.Where(x => Loaded.Contains(x.Source.Id)))
        {
            Chips.Add(new ModSourceViewModel(
                new ModSourceStatus(profile.Source, IsEnabled(profile.Source.Id), profile.Pins.Count, null),
                OnChipToggled,
                canUnload: true));
        }

        foreach (var savegame in _savegames.Where(x => Loaded.Contains(x.Source.Id)))
        {
            var summary = savegame.Summarize(view);

            Chips.Add(new ModSourceViewModel(
                new ModSourceStatus(savegame.Source, IsEnabled(savegame.Source.Id), summary.Listed, null),
                OnChipToggled,
                canUnload: true,
                detail: Describe(summary),
                foundCount: summary.Listed - summary.NotFound.Count));
        }

        OnPropertyChanged(nameof(CanAddSavegame));
    }

    /// <summary>The savegame chip's tooltip: what it lists, where each found mod comes from, and what was not found.</summary>
    private static string Describe(SavegameSourceSummary summary)
    {
        const int namesShown = 20;

        var lines = new List<string> { $"Listed: {summary.Listed:N0}" };

        lines.AddRange(summary.ByLocation.Select(x => $"{x.Location}: {x.Count:N0}"));

        if (summary.NotFound.Count > 0)
        {
            lines.Add($"Not found: {summary.NotFound.Count:N0}");
            lines.AddRange(summary.NotFound.Take(namesShown).Select(x => $"  {x.Value}"));

            if (summary.NotFound.Count > namesShown)
            {
                lines.Add($"  and {summary.NotFound.Count - namesShown:N0} more");
            }
        }

        return string.Join("\n", lines);
    }

    private void AddFolderChips(IEnumerable<ModSource> sources, IReadOnlyDictionary<ModSourceId, ModSourceStatus> scanned)
    {
        foreach (var source in sources)
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
    }

    /// <summary>
    /// Makes the game's mod folders the only sources enabled, for an editor opened to review drift.
    /// Answers with what should then be loaded.
    /// </summary>
    public ImmutableHashSet<ModSourceId> FocusOnTargets()
    {
        var targets = Targets;

        foreach (var other in Loaded.Except(targets))
        {
            _standby.Add(other);
        }

        // Whatever the remote sources offer is not on disk, and would both add updates the game did
        // not make and hide missing mods they happen to offer.
        foreach (var lookup in _remote)
        {
            _standby.Add(lookup.Id);
        }

        foreach (var target in targets)
        {
            _standby.Remove(target);

            if (Loaded.Contains(target) is false)
            {
                _catalog.Rescan(target);
            }
        }

        IncludeRegistered = false;

        return Loaded.Union(targets);
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

            _profiles.RemoveAll(x => x.Source.Id == id);
            _profiles.Add(new ProfileModSource(
                picked.Id,
                new ModSource(id, picked.Name, "Another profile in this repo.", ModSourceKind.Profile),
                await _otherProfiles.ReadPinsAsync(picked.Id, _cancellation)));

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
    /// Reads a savegame on this machine as a source, and makes it the only one enabled: the left list
    /// is then what the save lists and the profile does not pin, and <em>Not in the sources</em> is what
    /// the profile pins and the save does not list.
    /// </summary>
    [RelayCommand]
    private async Task AddSavegame()
    {
        try
        {
            var games = _repo.Games.Where(_savegameSlots.ListsMods).ToList();
            var options = new List<SavegameSourceOption>();

            foreach (var game in games)
            {
                foreach (var slot in await _savegameSlots.GetSlotsAsync(game, _cancellation))
                {
                    if (slot.IsOccupied && Loaded.Contains(ModSourceId.ForSavegame(game.Identity, slot.Ref)) is false)
                    {
                        options.Add(new SavegameSourceOption(game, slot, games.Count > 1));
                    }
                }
            }

            var modal = new PickSavegameSourceModalViewModel(options);

            await _modalService.Show(modal);

            if (modal.Result is not SavegameSourceOption picked)
            {
                return;
            }

            var savegame = new SavegameModSource(
                new ModSource(ModSourceId.ForSavegame(picked.Game.Identity, picked.Slot.Ref), picked.Label, picked.ToolTip, ModSourceKind.Savegame),
                ModSourceId.ForTarget(new ModTargetRef(picked.Game.Identity, picked.Slot.Ref.Target)),
                await ReadSavegameModsAsync(picked));

            _savegames.RemoveAll(x => x.Source.Id == savegame.Source.Id);
            _savegames.Add(savegame);

            await FocusOnSavegameAsync(savegame);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "reading a savegame's mod list as a source");
        }
    }

    private async Task<IReadOnlyList<SavegameModReference>> ReadSavegameModsAsync(SavegameSourceOption picked)
    {
        try
        {
            return await _savegameSlots.ReadModsAsync(picked.Game, picked.Slot.Ref, _cancellation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new UserFriendlyException($"Could not read the mods of {picked.Label}", inner: exception);
        }
    }

    /// <summary>
    /// Makes a savegame the only source enabled, with its mod folder loaded on standby so the files it
    /// is found in stay known.
    /// </summary>
    private Task FocusOnSavegameAsync(SavegameModSource savegame)
    {
        var id = savegame.Source.Id;
        var folder = savegame.ModFolder;

        foreach (var other in Loaded.Remove(id))
        {
            _standby.Add(other);
        }

        foreach (var lookup in _remote)
        {
            _standby.Add(lookup.Id);
        }

        IncludeRegistered = false;

        _standby.Remove(id);

        var loaded = Loaded.Add(id);

        if (loaded.Contains(folder) is false && _catalog.GetSources().Any(x => x.Id == folder))
        {
            _standby.Add(folder);
            _catalog.Rescan(folder);

            loaded = loaded.Add(folder);
        }

        return loaded.SetEquals(Loaded)
            ? _recompose()
            : _commit(_current() with { Loaded = loaded }, $"Loaded {savegame.Source.Name}");
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
                $"{ProfileModsEditorSummary.Mods(dependent.Count)} in your unsaved changes come only from {chip.Name} "
                    + "and will be removed.",
                IconKind.Question,
                "Unload",
                "Cancel");

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

    [RelayCommand]
    private async Task OpenInExplorer(ModSourceViewModel? chip)
    {
        if (chip is not { IsFolder: true })
        {
            return;
        }

        try
        {
            ExplorerFolder.Open(chip.Path);
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(
                new UserFriendlyException($"Could not open {chip.Name}", inner: exception),
                $"opening the source folder {chip.Path} in Explorer");
        }
    }


    private void OnChipToggled(ModSourceViewModel chip, bool enabled)
    {
        var id = chip.Source.Id;

        if (chip.IsRepo)
        {
            IncludeRegistered = enabled;
            _ = _recompose();
        }
        else if (enabled && _savegames.FirstOrDefault(x => x.Source.Id == id) is SavegameModSource savegame)
        {
            _ = FocusOnSavegameAsync(savegame);
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
