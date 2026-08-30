using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The repo's mods: one list over the catalog, whether a version is on disk, in the repo, or both.
/// </summary>
/// <remarks>
/// <para>
/// Import and Manage used to be sibling pages showing overlapping data under different rules, which
/// is the main thing about this area that confused. They are one list with presence filters, and
/// importing is a <em>selection mode</em> over it rather than a separate destination - same rows,
/// same template, one service. See docs/09-mod-catalog.md#manage.
/// </para>
/// <para>
/// The source list lives here too, so which folders are searched is adjustable in place rather than
/// being a fixed consequence of the repo's instances.
/// </para>
/// </remarks>
public partial class RepoModsPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ModCatalog _catalog;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly ModImportService _importService;
    private readonly IModalService _modalService;
    private readonly IDialogService _dialogService;
    private readonly IModsClient _modsClient;

    private readonly CancellationTokenSource _cancellation = new();
    private readonly ModRowActions _rowActions;

    private IReadOnlyList<ModListItemViewModel> _mods = [];
    private bool _suspendSelectionTracking;


    public RepoModsPageViewModel(
        Repo repo,
        ModCatalog.Factory catalogFactory,
        ModListItemViewModel.Factory itemFactory,
        ModImportService importService,
        IModalService modalService,
        IDialogService dialogService,
        IModsClient modsClient)
    {
        _repo = repo;
        _itemFactory = itemFactory;
        _importService = importService;
        _modalService = modalService;
        _dialogService = dialogService;
        _modsClient = modsClient;

        // The page owns the catalog and disposes it, so the per-source scan cache lives exactly as
        // long as the checkboxes that recompose from it.
        _catalog = catalogFactory.Create(repo);

        _rowActions = new ModRowActions(ReorderVersionsCommand, DeleteVersionCommand, DeleteModCommand);

        RepoName = repo.Name;
    }


    public string RepoName { get; }

    public ObservableCollection<ModSourceViewModel> Sources { get; } = [];

    [ObservableProperty]
    private ICollectionView? _modsView;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(HasMods))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(HasVisibleMods))]
    private int _visibleCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private int _selectedCount;

    /// <summary>True once loading has finished and the sources and the repo turned up nothing.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>
    /// Bulk import as a mode rather than a page: turning it on reveals the checkboxes and the footer
    /// bar, and turning it off puts the list back to something to read.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isSelectionMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isImporting;

    /// <summary>What the last import did, kept until the user asks for a fresh list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportSummary))]
    private string? _importSummary;

    [ObservableProperty]
    private ModPresenceFilter _presenceFilter = ModPresenceFilter.All;


    public bool HasMods => TotalCount > 0;
    public bool HasVisibleMods => VisibleCount > 0;
    public bool HasImportSummary => ImportSummary is not null;

    public string CountText => VisibleCount == TotalCount
        ? $"{TotalCount} mods"
        : $"{VisibleCount} of {TotalCount} mods";

    public string ImportButtonText => SelectedCount switch
    {
        0 => "Import selected",
        1 => "Import 1 mod",
        _ => $"Import {SelectedCount} mods"
    };


    [RelayCommand]
    private void SelectAll()
        => SetSelectionOfVisible(true);

    [RelayCommand]
    private void SelectNone()
        => SetSelectionOfVisible(false);

    [RelayCommand]
    private void ClearSearch()
        => SearchText = string.Empty;

    [RelayCommand]
    private async Task Refresh()
    {
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task RescanAll()
    {
        _catalog.RescanAll();

        await ReloadAsync();
    }

    [RelayCommand]
    private async Task RescanSource(ModSourceViewModel? source)
    {
        if (source is null)
        {
            return;
        }

        _catalog.Rescan(source.Source.Id);

        await ReloadAsync();
    }

    /// <summary>
    /// Adds a folder for this session only. Someone importing off a USB stick should not have that
    /// folder haunting the list for months, so nothing about it is written to disk.
    /// </summary>
    [RelayCommand]
    private async Task AddSource()
    {
        if (_dialogService.PickFolder(null) is not string path)
        {
            return;
        }

        _catalog.AddAdHocSource(path);

        await ReloadAsync();
    }

    [RelayCommand]
    private async Task RemoveSource(ModSourceViewModel? source)
    {
        if (source is null || source.IsAdHoc is false)
        {
            return;
        }

        _catalog.RemoveAdHocSource(source.Source.Id);

        await ReloadAsync();
    }


    #region Import

    [RelayCommand(CanExecute = nameof(CanImport), IncludeCancelCommand = true)]
    private async Task Import(CancellationToken cancellationToken)
    {
        var selected = _mods.Where(x => x.IsSelected).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        var rows = new Dictionary<ModVersionIdentity, ModListItemViewModel>();

        foreach (var row in selected)
        {
            row.ResetImportState();
            rows[row.Mod.Identity] = row;
        }

        ImportSummary = null;
        IsImporting = true;

        try
        {
            var request = new ModImportRequest(_repo.Id, [.. selected.Select(x => x.Mod)], _repo.Adapter.VersionComparer)
            {
                Progress = new RowProgressReporter(rows),
                ResolveArbitration = ResolveArbitrationAsync
            };

            // The overload that invalidates the catalog when it is over: a cancelled or partly failed
            // import still registered something, and a catalog that kept claiming otherwise would
            // offer those versions for import all over again.
            var result = await _importService.ImportAsync(_catalog, request, cancellationToken);

            foreach (var item in result.Items)
            {
                if (rows.TryGetValue(item.Identity, out var row))
                {
                    row.Apply(item);
                }
            }

            ImportSummary = Describe(result);
        }
        catch (OperationCanceledException)
        {
            // Whatever had already landed keeps its per-row result; the rest simply stops.
            ImportSummary = "Import cancelled. Anything already registered stayed registered.";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private bool CanImport()
        => IsSelectionMode && IsImporting is false && SelectedCount > 0;

    private static string Describe(ModImportResult result)
    {
        var registered = result.Items.Count(x => x.Status is ModImportStatus.Registered);
        var alreadyThere = result.Items.Count(x => x.Status is ModImportStatus.AlreadyRegistered);
        var failed = result.Items.Count(x => x.Status is ModImportStatus.Failed);
        var skipped = result.Unfinished.Count - failed;

        var parts = new List<string> { $"{registered} imported" };

        if (alreadyThere > 0)
        {
            parts.Add($"{alreadyThere} already in the repo");
        }

        if (skipped > 0)
        {
            parts.Add($"{skipped} skipped");
        }

        if (failed > 0)
        {
            parts.Add($"{failed} failed");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// One dialog for the whole import, and only for the mods the comparer could not settle.
    /// Everything it settled is already registering by the time this is asked.
    /// </summary>
    private async Task<IReadOnlyDictionary<ModKey, IReadOnlyList<ModVersionKey>>?> ResolveArbitrationAsync(
        IReadOnlyList<ModVersionArbitrationItem> items,
        CancellationToken cancellationToken)
    {
        // The import runs off the UI thread, and everything from here down is view models a
        // dispatcher-bound modal is about to render.
        return await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var modal = new ModVersionArbitrationModalViewModel(items);

            await _modalService.Show(modal);

            return modal.Result;
        }).Task.Unwrap();
    }

    #endregion


    #region Managing what the repo holds

    /// <summary>
    /// The manual reorder - the backstop for an order that is wrong for reasons optimistic
    /// concurrency cannot catch, such as a comparer that guessed badly or an arbitration someone
    /// regrets. The same control the arbitration dialog uses, over the same operation.
    /// </summary>
    [RelayCommand]
    private async Task ReorderVersions(ModListItemViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var versions = _mods
            .Where(x => x.Mod.ModId == row.Mod.ModId && x.Mod.IsOnServer)
            .OrderBy(x => x.Mod.SequenceNumber)
            .Select(x => x.Mod.VersionId)
            .ToList();

        var modal = new ModVersionReorderModalViewModel(row.Name, _repo.Id, row.Mod.ModId, versions, _modsClient);

        await _modalService.Show(modal);

        if (modal.Saved)
        {
            await ReloadAfterServerChangeAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteVersion(ModListItemViewModel? row)
    {
        if (row is null || row.IsOnServer is false)
        {
            return;
        }

        var confirmation = new ConfirmationDialogViewModel(
            "Really?",
            $"Delete version '{row.Version}' of '{row.Name}' from the repo?\n"
                + "The file goes with it, and this cannot be undone.",
            IconKind.Warning,
            "Delete version",
            "Keep");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        try
        {
            await _modsClient.DeleteModVersionV1Async(_repo.Id, row.Id, row.Version, _cancellation.Token);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.ModInUse)
        {
            await ShowRefusal("Still in use",
                $"A profile depends on version '{row.Version}' of '{row.Name}'. Take it out of that profile first.");

            return;
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.CannotDeleteOnlyModVersion)
        {
            // A mod with no versions is not a state anything else in the system can represent, which
            // is why removing the last one is its own action rather than the same one again.
            await ShowRefusal("That is the only version",
                $"'{row.Name}' has no other version, so this would leave the mod with none. Delete the whole mod instead.");

            return;
        }

        await ReloadAfterServerChangeAsync();
    }

    [RelayCommand]
    private async Task DeleteMod(ModListItemViewModel? row)
    {
        if (row is null || row.IsOnServer is false)
        {
            return;
        }

        var versionCount = _mods.Count(x => x.Mod.ModId == row.Mod.ModId && x.Mod.IsOnServer);

        var confirmation = new ConfirmationDialogViewModel(
            "Really?",
            $"Delete '{row.Name}' and all {versionCount} of its versions from the repo?\n"
                + "The files go with them, and this cannot be undone.",
            IconKind.Warning,
            "Delete mod",
            "Keep");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        try
        {
            await _modsClient.DeleteModV1Async(_repo.Id, row.Id, _cancellation.Token);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.ModInUse)
        {
            await ShowRefusal("Still in use",
                $"A profile depends on '{row.Name}'. Take it out of that profile first.");

            return;
        }

        await ReloadAfterServerChangeAsync();
    }

    private Task ShowRefusal(string title, string message)
    {
        return _modalService.Show(ConfirmationDialogViewModel.Refusal(title, message));
    }

    /// <summary>
    /// A delta fetch only ever adds, so a version that has just been deleted is invisible to one -
    /// which is exactly the change that was made.
    /// </summary>
    private async Task ReloadAfterServerChangeAsync()
    {
        _catalog.ReloadRegisteredMods();

        await ReloadAsync();
    }

    #endregion


    /// <summary>
    /// Called when the user navigates away. Stops this page waiting on the catalog, and cancels the
    /// scans it owns - a mod folder walk is the most expensive thing this app does and nobody is
    /// waiting for it any more.
    /// </summary>
    public void Dispose()
    {
        // Deliberately not disposed: the wait may still be inside the token's registration, and
        // disposing a source out from under that is not safe. Nothing here holds a wait handle,
        // so letting it be collected costs nothing.
        _cancellation.Cancel();
        _catalog.Dispose();
    }

    /// <summary>
    /// A cancelled scan is the expected outcome of navigating away, not something to show the user
    /// an error modal about.
    /// </summary>
    protected override void OnInitFailed(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return;
        }

        base.OnInitFailed(ex);
    }


    protected override Task InitAsync()
        => LoadAsync();


    private async Task ReloadAsync()
    {
        IsLoading = true;

        try
        {
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
            // Navigating away mid-reload.
        }
        finally
        {
            // Publish clears this on the way through; the finally is for the paths that never reach
            // it, so a failed reload does not leave the list claiming to still be reading.
            IsLoading = false;
        }
    }

    private async Task LoadAsync()
    {
        var snapshot = await _catalog.GetAsync(_cancellation.Token);

        // The rows and the collection view are WPF-facing, and this may well have arrived on a
        // thread-pool thread.
        await Application.Current.Dispatcher.InvokeAsync(() => Publish(snapshot));
    }

    private void Publish(ModCatalogSnapshot snapshot)
    {
        foreach (var mod in _mods)
        {
            mod.PropertyChanged -= OnModPropertyChanged;
        }

        Sources.Clear();

        foreach (var status in snapshot.Sources)
        {
            Sources.Add(new ModSourceViewModel(status, OnSourceEnabledChanged));
        }

        // With a single source every row would name the same one, which is just noise.
        var showSources = snapshot.Sources.Count(x => x.IsEnabled) > 1;

        _mods = [.. snapshot.Versions
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.VersionId.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => CreateRow(x, showSources))];

        // Rebuilt rather than refreshed, because the list behind it is replaced wholesale - adding
        // a couple of thousand rows to a bound observable collection one at a time is a couple of
        // thousand layout passes.
        var view = CollectionViewSource.GetDefaultView(_mods);
        view.Filter = x => x is ModListItemViewModel mod && Passes(mod);

        ModsView = view;
        TotalCount = _mods.Count;
        // Respects anything typed into the search box while the scan was still running.
        VisibleCount = _mods.Count(Passes);
        IsEmpty = _mods.Count == 0;
        IsLoading = false;

        RecountSelection();
    }

    private ModListItemViewModel CreateRow(CatalogModVersion version, bool showSources)
    {
        var item = _itemFactory.Create(_repo.Id, version);

        item.Status = version.GetImportStatus();
        item.IsSelectable = IsSelectionMode;

        // Only a registered version has anything to reorder or delete.
        item.Actions = version.IsOnServer ? _rowActions : null;

        if (showSources && version.FoundIn.Count > 0)
        {
            item.Sources = string.Join(", ", version.FoundIn.Select(source => source.Source.Name));
        }

        item.PropertyChanged += OnModPropertyChanged;

        return item;
    }

    private bool Passes(ModListItemViewModel mod)
    {
        return mod.Matches(SearchText) && PresenceFilter switch
        {
            ModPresenceFilter.InRepo => mod.Mod.IsOnServer,
            ModPresenceFilter.OnDiskOnly => mod.Mod.IsLocal && mod.Mod.IsOnServer is false,
            ModPresenceFilter.Unused => mod.Mod.IsUnused,
            _ => true
        };
    }


    private void OnSourceEnabledChanged(ModSourceViewModel source, bool enabled)
    {
        _catalog.SetEnabled(source.Source, enabled);

        // Recomposes from the scans already in memory, so this is instant for a source that has been
        // read once - which is the whole reason the catalog caches per source.
        RefreshCommand.Execute(null);
    }

    partial void OnSearchTextChanged(string value)
        => RefilterVisible();

    partial void OnPresenceFilterChanged(ModPresenceFilter value)
        => RefilterVisible();

    partial void OnIsSelectionModeChanged(bool value)
    {
        _suspendSelectionTracking = true;

        try
        {
            foreach (var mod in _mods)
            {
                mod.IsSelectable = value;

                if (value is false)
                {
                    mod.IsSelected = false;
                }
            }
        }
        finally
        {
            _suspendSelectionTracking = false;
        }

        RecountSelection();
    }

    private void RefilterVisible()
    {
        ModsView?.Refresh();
        VisibleCount = _mods.Count(Passes);
    }

    private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ModListItemViewModel.IsSelected) || _suspendSelectionTracking)
        {
            return;
        }

        RecountSelection();
    }

    /// <summary>
    /// Only what the filters are currently showing, so "select all" can never pick up a mod the user
    /// cannot see.
    /// </summary>
    private void SetSelectionOfVisible(bool isSelected)
    {
        _suspendSelectionTracking = true;

        try
        {
            foreach (var mod in _mods.Where(Passes))
            {
                mod.IsSelected = isSelected;
            }
        }
        finally
        {
            _suspendSelectionTracking = false;
        }

        RecountSelection();
    }

    private void RecountSelection()
        => SelectedCount = _mods.Count(x => x.IsSelected);


    /// <summary>
    /// Per row, not per import: at two thousand mods a single global spinner cannot tell a working
    /// import from a hung one.
    /// </summary>
    /// <remarks>
    /// Byte counts arrive thousands of times per file, on whatever thread is doing the upload, so
    /// anything finer than a whole percent is redraw nobody can see. WPF marshals the property
    /// changes themselves, which is why this does not dispatch.
    /// </remarks>
    private sealed class RowProgressReporter(IReadOnlyDictionary<ModVersionIdentity, ModListItemViewModel> rows)
        : IProgress<ModImportProgress>
    {
        private readonly ConcurrentDictionary<ModVersionIdentity, int> _lastPercent = new();


        public void Report(ModImportProgress value)
        {
            if (rows.TryGetValue(value.Identity, out var row) is false)
            {
                return;
            }

            if (value.Phase is ModImportPhase.Uploading && row.IsUploading)
            {
                var percent = value.TotalBytes > 0
                    ? (int)(value.BytesTransferred * 100 / value.TotalBytes)
                    : 0;

                if (_lastPercent.TryGetValue(value.Identity, out var last) && last == percent)
                {
                    return;
                }

                _lastPercent[value.Identity] = percent;
            }

            row.Apply(value);
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoModsPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoModsPageViewModel>(serviceProvider, repo);
    }
}
