using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Where the profile mod editor's versions come from: the repo, the game's folders, Downloads, folders
/// added for the session, and other profiles - as a row of chips.
/// </summary>
/// <remarks>
/// Which sources are loaded is part of the editor's undo history, so it is read from the page and
/// changed through it. Whether a loaded source is switched on is not, and lives here.
/// </remarks>
public sealed partial class EditorSourcesViewModel(
    ModCatalog catalog,
    OtherProfilesReader otherProfiles,
    IDialogService dialogService,
    IModalService modalService,
    IErrorReporter errorReporter,
    Func<ProfileEditorSnapshot> current,
    Func<ProfileEditorSnapshot, string, Task> commit,
    Func<Task> recompose,
    CancellationToken cancellation) : ObservableObject
{
    private readonly HashSet<ModSourceId> _standby = [];
    private readonly Dictionary<ModSourceId, ProfileModSource> _profiles = [];

    private ProfileEditorCatalog? _view;


    public ObservableCollection<ModSourceViewModel> Chips { get; } = [];

    /// <summary>Whether the repo's registered versions are among what the left list offers.</summary>
    public bool IncludeRegistered { get; private set; } = true;

    /// <summary>Whether anything at all is being offered. With nothing, <em>Not in the sources</em> means nothing.</summary>
    [ObservableProperty]
    private bool _hasEnabledSources = true;

    /// <summary>Whether any folder is being read, which is whether "no updates" has looked anywhere but the repo.</summary>
    public bool HasEnabledFolders => catalog.GetSources().Any(x => IsEnabled(x.Id));

    public IReadOnlyList<ProfileModSource> EnabledProfiles => [.. _profiles.Values.Where(x => IsEnabled(x.Source.Id))];

    private ImmutableHashSet<ModSourceId> Loaded => current().Loaded;


    /// <summary>Tells the catalog which of its sources are enabled, on standby or unloaded.</summary>
    public void ApplyToCatalog()
    {
        foreach (var source in catalog.GetSources())
        {
            catalog.SetState(source.Id, StateOf(source.Id));
        }

        HasEnabledSources = IncludeRegistered || HasEnabledFolders || _profiles.Keys.Any(IsEnabled);
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

        var scanned = view.Snapshot.Sources.ToDictionary(x => x.Source.Id);

        foreach (var source in catalog.GetSources())
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
            catalog.Rescan(id);
        }

        return Loaded.Add(id);
    }

    /// <summary>Switches Downloads on, loading it if it is not.</summary>
    public Task EnableDownloadsAsync()
        => IsEnabled(ModSourceId.Downloads) ? Task.CompletedTask : LoadAsync(ModSourceId.Downloads, "Downloads");


    [RelayCommand]
    private Task RescanAll()
    {
        catalog.RescanAll();

        return recompose();
    }

    /// <summary>Adds a folder for this session only. Nothing about it is written to disk.</summary>
    [RelayCommand]
    private Task AddFolder()
    {
        if (dialogService.PickFolder(null) is not string path)
        {
            return Task.CompletedTask;
        }

        var source = catalog.AddAdHocSource(path);

        catalog.Rescan(source.Id);

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
            var others = (await otherProfiles.ListAsync(cancellation))
                .Where(x => Loaded.Contains(ModSourceId.ForProfile(x.Id)) is false)
                .ToList();

            var modal = new PickProfileSourceModalViewModel(others);

            await modalService.Show(modal);

            if (modal.Result is not ProfileDto picked)
            {
                return;
            }

            var id = ModSourceId.ForProfile(picked.Id);

            _profiles[id] = new ProfileModSource(
                picked.Id,
                new ModSource(id, picked.Name, "Another profile in this repo.", ModSourceKind.Profile),
                await otherProfiles.ReadPinsAsync(picked.Id, cancellation));

            await LoadAsync(id, picked.Name);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await errorReporter.ShowAsync(exception, "reading another profile's mod list as a source");
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

        var snapshot = current();
        var dependent = _view?.PinsOnlyIn(snapshot.Draft, chip.Source.Id) ?? [];

        if (dependent.Count > 0)
        {
            var confirmation = new ConfirmationDialogViewModel(
                $"Unload {chip.Name}?",
                $"{ProfileModsEditorSummary.Mods(dependent.Count)} in your draft come only from {chip.Name} "
                    + "and will be taken out with it.",
                IconKind.Question,
                "Unload",
                "Keep it");

            await modalService.Show(confirmation);

            if (confirmation.Result is false)
            {
                return;
            }
        }

        await commit(
            new ProfileEditorSnapshot(snapshot.Draft.Remove(dependent), snapshot.Loaded.Remove(chip.Source.Id)),
            $"Unloaded {chip.Name}");
    }


    private void OnChipToggled(ModSourceViewModel chip, bool enabled)
    {
        var id = chip.Source.Id;

        if (chip.IsRepo)
        {
            IncludeRegistered = enabled;
            _ = recompose();
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

            _ = recompose();
        }
        else if (enabled)
        {
            catalog.Rescan(id);
            _ = LoadAsync(id, chip.Name);
        }
    }

    private Task LoadAsync(ModSourceId id, string name)
    {
        _standby.Remove(id);

        return Loaded.Contains(id)
            ? recompose()
            : commit(current() with { Loaded = Loaded.Add(id) }, $"Loaded {name}");
    }

    private ModSourceState StateOf(ModSourceId id)
        => Loaded.Contains(id) is false
            ? ModSourceState.Unloaded
            : _standby.Contains(id) ? ModSourceState.Standby : ModSourceState.Enabled;

    private bool IsEnabled(ModSourceId id) => StateOf(id) is ModSourceState.Enabled;
}
