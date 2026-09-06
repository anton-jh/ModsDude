using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Helpers;
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
/// The repo's mods: what the enabled sources hold on the left, what the repo holds on the right, and
/// importing as the move between them.
/// </summary>
/// <remarks>
/// <para>
/// Import and Manage used to be sibling pages showing overlapping data under different rules, which
/// is the main thing about this area that confused. They are one page, laid out like the profile mod
/// editor - the same two lists, the same source pane under the left one, the same "nothing is
/// uploaded until you press the button" rule - because they are the same act: deciding what a
/// collection should hold and then writing it. See docs/09-mod-catalog.md#manage.
/// </para>
/// <para>
/// <b>A mod is never on both sides at once.</b> The left list is what the sources hold and the repo
/// does not; moving a row rightwards is a queued import, and the row it becomes is the very same row
/// object, so its icon and its import marks carry across.
/// </para>
/// <para>
/// The source list lives under the left list, so which folders are searched is adjustable in place
/// rather than being a fixed consequence of the repo's instances.
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

    /// <summary>Everything the sources hold that the repo does not, queued or not.</summary>
    private IReadOnlyList<ModListItemViewModel> _local = [];

    /// <summary>What the repo holds, rebuilt from the catalog and never added to by the user.</summary>
    private IReadOnlyList<ModListItemViewModel> _registered = [];

    /// <summary>
    /// The right-hand list: the registered rows plus whatever has been queued for import. Replaced
    /// wholesale where the whole list changes and mutated where one row moves, which is what keeps a
    /// single click cheap without paying a couple of thousand collection events for a reload.
    /// </summary>
    private ObservableCollection<ModListItemViewModel> _repoMods = [];

    /// <summary>
    /// What has been moved rightwards, held as identities rather than rows: a rescan builds new rows
    /// for the same files, and the queue has to survive that.
    /// </summary>
    private readonly HashSet<ModVersionIdentity> _queued = [];

    /// <summary>
    /// The newest version the repo holds of each mod it holds at all - what "an update" is measured
    /// against, and the only thing on this page that needs the repo's version ordering.
    /// </summary>
    private IReadOnlyDictionary<ModKey, ModVersionKey> _newestRegistered =
        new Dictionary<ModKey, ModVersionKey>();


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

        // A guest can read the catalog - every GET here is theirs - but everything that writes to the
        // repo needs Member. The commands refuse, and the note is what the refused buttons say.
        CanModify = repo.MembershipLevel >= RepoMembershipLevel.Member;
        ModifyRestriction = CanModify
            ? null
            : "Guests cannot change a repo's mods. Ask an admin for a higher membership level.";

        _rowActions = new ModRowActions(
            ReorderVersionsCommand, DeleteVersionCommand, DeleteModCommand, ModifyRestriction);

        RepoName = repo.Name;
    }


    public string RepoName { get; }

    public ObservableCollection<ModSourceViewModel> Sources { get; } = [];

    /// <summary>
    /// Whether this user may write to the repo's mods. The page itself is open to a guest - browsing
    /// the catalog, searching it and filtering it are all theirs - and only importing, reordering and
    /// deleting are refused. A guest gets the right-hand list alone, because the left one exists to
    /// feed an import they cannot make.
    /// </summary>
    public bool CanModify { get; }

    /// <summary>Why those are refused, shown on the page. Null where they are not.</summary>
    public string? ModifyRestriction { get; }

    /// <summary>The sources' half: on disk, and not registered here.</summary>
    [ObservableProperty]
    private ICollectionView? _localView;

    /// <summary>The repo's half, plus the rows queued to join it.</summary>
    [ObservableProperty]
    private ICollectionView? _repoView;

    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>Filters both lists, because a mod is only ever in one of them.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Narrows the right-hand list to what a delete would be accepted for. The one presence filter
    /// worth keeping now that the lists say the rest: registered-or-not is which side a row is on.
    /// </summary>
    [ObservableProperty]
    private bool _unusedOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocalCountText))]
    [NotifyPropertyChangedFor(nameof(HasVisibleLocalMods))]
    private int _localCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocalCountText))]
    [NotifyPropertyChangedFor(nameof(HasLocalMods))]
    [NotifyCanExecuteChangedFor(nameof(QueueAllCommand))]
    private int _localTotal;

    /// <summary>
    /// How many of the left list's rows are newer versions of mods the repo already holds. Counted
    /// over the whole list rather than what the search is showing, because that is what the button
    /// beside the count acts on.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateButtonText))]
    [NotifyCanExecuteChangedFor(nameof(QueueAllUpdatesCommand))]
    private int _updateCount;

    /// <summary>
    /// The wider set the same button's menu offers: every version of a mod the repo holds that it
    /// does not have, whether or not the ordering makes it newer. A superset of
    /// <see cref="UpdateCount"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnregisteredButtonText))]
    [NotifyPropertyChangedFor(nameof(HasUnregisteredVersions))]
    [NotifyCanExecuteChangedFor(nameof(QueueAllUnregisteredCommand))]
    private int _unregisteredCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepoCountText))]
    [NotifyPropertyChangedFor(nameof(HasVisibleRepoMods))]
    private int _repoCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepoCountText))]
    [NotifyPropertyChangedFor(nameof(HasRepoMods))]
    private int _repoTotal;

    /// <summary>How many rows on the right are waiting to be uploaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueuedText))]
    [NotifyPropertyChangedFor(nameof(HasQueued))]
    [NotifyPropertyChangedFor(nameof(ImportButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    private int _queuedCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(QueueAllCommand))]
    private bool _isImporting;

    /// <summary>What the last import did, kept until the user asks for a fresh list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportSummary))]
    private string? _importSummary;


    public bool HasLocalMods => LocalTotal > 0;
    public bool HasVisibleLocalMods => LocalCount > 0;
    public bool HasRepoMods => RepoTotal > 0;
    public bool HasVisibleRepoMods => RepoCount > 0;
    public bool HasQueued => QueuedCount > 0;
    public bool HasImportSummary => ImportSummary is not null;

    public string LocalCountText => Describe(LocalCount, LocalTotal);
    public string RepoCountText => Describe(RepoCount, RepoTotal);

    public string QueuedText => QueuedCount == 1
        ? "1 mod will be imported when you press Import"
        : $"{QueuedCount} mods will be imported when you press Import";

    public string ImportButtonText => QueuedCount switch
    {
        0 => "Import",
        1 => "Import 1 mod",
        _ => $"Import {QueuedCount} mods"
    };

    /// <summary>The count lives on the button, which is the only place it would be acted on.</summary>
    public string UpdateButtonText => UpdateCount switch
    {
        0 => "Add all updates",
        1 => "Add 1 update",
        _ => $"Add {UpdateCount} updates"
    };

    public bool HasUnregisteredVersions => UnregisteredCount > 0;

    public string UnregisteredButtonText => UnregisteredCount switch
    {
        0 => "Add unregistered versions",
        1 => "Add 1 unregistered version",
        _ => $"Add {UnregisteredCount} unregistered versions"
    };

    /// <summary>
    /// Worded with what it takes that the primary does not, because that is the whole difference
    /// between the two - and an older version is a deliberate thing to want, not a mistake.
    /// </summary>
    public string UnregisteredDescription =>
        "Every version the repo does not hold, of every mod it does - including ones older than its "
            + "newest, and ones whose order this game's comparer could not settle.";


    #region Moving mods between the lists

    /// <summary>
    /// Queues one mod for import. Nothing is uploaded here - the row simply changes sides, which is
    /// what makes taking it back free.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanModify))]
    private void Queue(ModListItemViewModel? row)
    {
        if (row is null || row.Mod.IsOnServer || _queued.Add(row.Mod.Identity) is false)
        {
            return;
        }

        InsertSorted(row);
        RefreshLeft();
    }

    /// <summary>Takes one queued mod back, leaving the repo exactly as it was.</summary>
    [RelayCommand(CanExecute = nameof(CanModify))]
    private void Unqueue(ModListItemViewModel? row)
    {
        if (row is null || _queued.Remove(row.Mod.Identity) is false)
        {
            return;
        }

        row.ResetImportState();

        _repoMods.Remove(row);
        RefreshLeft();
    }

    /// <summary>
    /// Everything the left list is currently showing, so a search is how a subset is picked. Rebuilds
    /// the right-hand list rather than adding a couple of thousand rows to it one at a time.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanQueueAll))]
    private void QueueAll()
    {
        foreach (var row in _local.Where(PassesLocal))
        {
            _queued.Add(row.Mod.Identity);
        }

        RebuildRepoMods();
    }

    private bool CanQueueAll() => CanModify && IsImporting is false && LocalTotal > 0;

    /// <summary>
    /// Every mod on the left that the repo already holds an older version of - the common errand,
    /// which is otherwise picking a handful of rows out of a folder of five hundred. Not limited to
    /// what the search is showing: an update is a fact about the repo, not about the view, and the
    /// count on the button says the same.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanQueueAllUpdates))]
    private void QueueAllUpdates()
        => QueueEvery(IsUpdate);

    private bool CanQueueAllUpdates() => CanModify && IsImporting is false && UpdateCount > 0;

    /// <summary>
    /// The wider version of the same errand, one click further in: every version of a mod the repo
    /// holds that it does not have, older ones included.
    /// </summary>
    /// <remarks>
    /// Behind the caret rather than beside it because the common case is catching up to what a mod
    /// author has released, and that is what "update" means. Filling in the older versions is a real
    /// thing to want - a profile can pin any of them, and a repo missing the version a teammate is
    /// on cannot be joined - it is just not the thing anyone comes here for daily.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanQueueAllUnregistered))]
    private void QueueAllUnregistered()
        => QueueEvery(IsUnregisteredVersionOfKnownMod);

    private bool CanQueueAllUnregistered() => CanModify && IsImporting is false && UnregisteredCount > 0;

    private void QueueEvery(Func<CatalogModVersion, bool> predicate)
    {
        foreach (var row in _local.Where(x => _queued.Contains(x.Mod.Identity) is false && predicate(x.Mod)))
        {
            _queued.Add(row.Mod.Identity);
        }

        RebuildRepoMods();
    }

    /// <summary>
    /// A version of a mod the repo holds, that the repo's own ordering puts after everything it
    /// holds of it. The comparer abstains rather than guesses, so a version string it cannot place
    /// is not an update - it is one of the versions behind the caret, and importing it is what asks
    /// the user where it goes.
    /// </summary>
    private bool IsUpdate(CatalogModVersion version)
    {
        return IsUnregisteredVersionOfKnownMod(version)
            && _repo.Adapter.VersionComparer.Compare(version.VersionId, _newestRegistered[version.ModId])
                is ModVersionComparison.Later;
    }

    /// <summary>Any version the repo lacks, of a mod it already has - whatever the ordering says.</summary>
    private bool IsUnregisteredVersionOfKnownMod(CatalogModVersion version)
        => version.IsOnServer is false && _newestRegistered.ContainsKey(version.ModId);

    /// <summary>
    /// Throws the queue away. Only what is still waiting: a mod this page has already imported
    /// belongs to the repo now, whatever the list still says about the run.
    /// </summary>
    /// <remarks>
    /// Asked about rather than done, because a queue can be a couple of thousand rows deep and there
    /// is no undo - and answered by pointing out that this is free, which is the whole reason nothing
    /// is uploaded until Import.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    private async Task DiscardChanges()
    {
        var confirmation = new ConfirmationDialogViewModel(
            "Discard changes?",
            "The mods waiting to be imported have not been uploaded, so nothing in the repo changes.",
            IconKind.Question,
            "Discard",
            "Keep them");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        foreach (var row in _repoMods.Where(IsPending))
        {
            row.ResetImportState();

            _queued.Remove(row.Mod.Identity);
        }

        RebuildRepoMods();
    }

    private bool CanDiscardChanges() => IsImporting is false && QueuedCount > 0;

    /// <summary>
    /// Waiting to be uploaded: on the right, not in the repo, and not something a run has already
    /// put there. Derived from the row rather than tracked separately, so a finished import cannot
    /// leave the two disagreeing.
    /// </summary>
    private static bool IsPending(ModListItemViewModel row)
        => row.Mod.IsOnServer is false && row.ImportState is not ModImportRowState.Succeeded;

    #endregion


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
        var pending = _repoMods.Where(IsPending).ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var rows = new Dictionary<ModVersionIdentity, ModListItemViewModel>();

        foreach (var row in pending)
        {
            row.ResetImportState();
            rows[row.Mod.Identity] = row;
        }

        ImportSummary = null;
        IsImporting = true;

        try
        {
            var request = new ModImportRequest(_repo.Id, [.. pending.Select(x => x.Mod)], _repo.Adapter.VersionComparer)
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

            // One dialog for the run, once every row has been marked - so what it names is already
            // findable in the list behind it.
            if (ModImportProblems.Build(result, id => NameOf(rows, id)) is ConfirmationDialogViewModel problems)
            {
                await _modalService.Show(problems);
            }
        }
        catch (OperationCanceledException)
        {
            // Whatever had already landed keeps its per-row result; the rest simply stops.
            ImportSummary = "Import cancelled. Anything already registered stayed registered.";
        }
        finally
        {
            IsImporting = false;

            // Re-sorted exactly once, now that every row has its outcome: what did not make it comes
            // to the top, where it can be read and tried again. What is still waiting is what the run
            // did not finish, so the button now offers exactly that retry, and the rows that did land
            // stay in the list, marked, until it is refreshed.
            RebuildRepoMods();
        }
    }

    private bool CanImport()
        => CanModify && IsImporting is false && QueuedCount > 0;

    /// <summary>
    /// The row's own name, falling back to the mod's id for a version that is somehow no longer in
    /// the list - a name the user will not recognise beats a blank in a list of what went wrong.
    /// </summary>
    private static string NameOf(IReadOnlyDictionary<ModVersionIdentity, ModListItemViewModel> rows, ModVersionIdentity id)
    {
        return rows.TryGetValue(id, out var row) ? row.Name : id.ModId.Value;
    }

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
    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task ReorderVersions(ModListItemViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var versions = _registered
            .Where(x => x.Mod.ModId == row.Mod.ModId)
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

    [RelayCommand(CanExecute = nameof(CanModify))]
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

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DeleteMod(ModListItemViewModel? row)
    {
        if (row is null || row.IsOnServer is false)
        {
            return;
        }

        var versionCount = _registered.Count(x => x.Mod.ModId == row.Mod.ModId);

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

        // The rows and the collection views are WPF-facing, and this may well have arrived on a
        // thread-pool thread.
        await Application.Current.Dispatcher.InvokeAsync(() => Publish(snapshot));
    }

    private void Publish(ModCatalogSnapshot snapshot)
    {
        Sources.Clear();

        foreach (var status in snapshot.Sources)
        {
            Sources.Add(new ModSourceViewModel(status, OnSourceEnabledChanged));
        }

        // With a single source every row would name the same one, which is just noise.
        var showSources = snapshot.Sources.Count(x => x.IsEnabled) > 1;

        var registered = new List<CatalogModVersion>();
        var local = new List<CatalogModVersion>();

        foreach (var version in Order(snapshot.Versions))
        {
            // A guest gets no left list at all, so the rows for it are not built either. Nothing they
            // can see is on disk only - the import the left list feeds is refused - and a list whose
            // every row offers a button that is not there would be a list of dead ends.
            if (version.IsOnServer is false && CanModify is false)
            {
                continue;
            }

            (version.IsOnServer ? registered : local).Add(version);
        }

        // Read from the repo's own stored order rather than re-derived: it was arbitrated once and
        // saved, and a client recomputing it would disagree with the repo about what the newest
        // version is. Built before the rows, which ask it what counts as an update.
        _newestRegistered = registered
            .GroupBy(x => x.ModId)
            .ToDictionary(x => x.Key, x => x.MaxBy(version => version.SequenceNumber)!.VersionId);

        _registered = [.. registered.Select(x => CreateRow(x, showSources))];
        _local = [.. local.Select(x => CreateRow(x, showSources))];

        // A queue survives a rescan, but only for the versions the sources still hold: a file that
        // has gone has nothing left to upload, and a row for it could only fail.
        _queued.IntersectWith(local.Select(x => x.Identity));

        // Rebuilt rather than refreshed, because the list behind it is replaced wholesale - adding a
        // couple of thousand rows to a bound observable collection one at a time is a couple of
        // thousand layout passes.
        var localView = CollectionViewSource.GetDefaultView(_local);
        localView.Filter = x => x is ModListItemViewModel row && PassesLocal(row);

        LocalView = localView;

        RebuildRepoMods();

        IsLoading = false;
    }

    /// <summary>
    /// The right-hand list from scratch: what the repo holds, plus whatever is queued to join it, in
    /// one order.
    /// </summary>
    private void RebuildRepoMods()
    {
        _repoMods = [.. _registered.Concat(_local.Where(x => _queued.Contains(x.Mod.Identity))).OrderBy(x => x, RowOrder)];

        var view = CollectionViewSource.GetDefaultView(_repoMods);
        view.Filter = x => x is ModListItemViewModel row && PassesRepo(row);

        RepoView = view;

        RefreshLists();
    }

    /// <summary>
    /// Puts one queued row where a rebuild would have put it, so a single click costs one insert
    /// rather than a new list and a new view - and, more to the point, leaves the scroll position
    /// alone.
    /// </summary>
    private void InsertSorted(ModListItemViewModel row)
    {
        var index = 0;

        while (index < _repoMods.Count && RowOrder.Compare(_repoMods[index], row) <= 0)
        {
            index++;
        }

        _repoMods.Insert(index, row);
    }

    private ModListItemViewModel CreateRow(CatalogModVersion version, bool showSources)
    {
        var item = _itemFactory.Create(_repo.Id, version);

        // Which side the row is on already says whether the repo holds it, so the presence chip would
        // repeat the list it is in on every single row. The one thing neither list says is that a row
        // is a newer version of something already in the repo, and Recount marks those.
        item.Status = ModDisplayStatus.None;
        item.IsSelectable = false;

        // Only a registered version has anything to reorder or delete.
        item.Actions = version.IsOnServer ? _rowActions : null;

        if (showSources && version.FoundIn.Count > 0)
        {
            item.Sources = string.Join(", ", version.FoundIn.Select(source => source.Source.Name));
        }

        return item;
    }

    private bool PassesLocal(ModListItemViewModel row)
        => _queued.Contains(row.Mod.Identity) is false && row.Matches(SearchText);

    private bool PassesRepo(ModListItemViewModel row)
    {
        // A queued row is never "unused" - the repo has no dependency that could name it - and hiding
        // what was just moved across, while the button below counts it, would be the filter arguing
        // with the button.
        return row.Matches(SearchText)
            && (UnusedOnly is false || row.Mod.IsUnused || row.Mod.IsOnServer is false);
    }

    private static string Describe(int visible, int total)
        => visible == total ? $"{total} mods" : $"{visible} of {total} mods";

    private static IEnumerable<CatalogModVersion> Order(IEnumerable<CatalogModVersion> versions)
        => versions
            .OrderBy(x => x.Name, NaturalOrder.Comparer)
            .ThenBy(x => x.VersionId.Value, NaturalOrder.Comparer);

    /// <summary>
    /// The order the right-hand list is held in, and the one an insert has to agree with: whatever
    /// wants an answer first, then alphabetical.
    /// </summary>
    private static readonly IComparer<ModListItemViewModel> RowOrder =
        Comparer<ModListItemViewModel>.Create((left, right) =>
        {
            var byRank = Rank(left).CompareTo(Rank(right));

            if (byRank != 0)
            {
                return byRank;
            }

            var byName = NaturalOrder.Compare(left.Name, right.Name);

            return byName != 0
                ? byName
                : NaturalOrder.Compare(left.Version, right.Version);
        });

    /// <summary>
    /// How near the top a row belongs. What went wrong first, then what is still to happen, then the
    /// repo itself - because the top of a two thousand row list is the only part of it anyone reads
    /// after an import, and a failure buried at "S" is a failure nobody sees.
    /// </summary>
    /// <remarks>
    /// Read only when the list is built, never live: rows changing rank mid-import would reshuffle
    /// the list under the pointer while it is being watched. The import re-sorts once, when it is
    /// over.
    /// </remarks>
    private static int Rank(ModListItemViewModel row) => row.ImportState switch
    {
        ModImportRowState.Failed => 0,
        ModImportRowState.Skipped => 1,
        ModImportRowState.Running => 2,
        ModImportRowState.Succeeded => 4,
        _ => row.Mod.IsOnServer ? 5 : 3
    };


    private void OnSourceEnabledChanged(ModSourceViewModel source, bool enabled)
    {
        _catalog.SetEnabled(source.Source, enabled);

        // Recomposes from the scans already in memory, so this is instant for a source that has been
        // read once - which is the whole reason the catalog caches per source.
        RefreshCommand.Execute(null);
    }

    partial void OnSearchTextChanged(string value)
        => RefreshLists();

    partial void OnUnusedOnlyChanged(bool value)
        => RefreshLists();

    private void RefreshLists()
    {
        RepoView?.Refresh();

        RefreshLeft();
    }

    /// <summary>
    /// The left list alone, for a row that has changed sides. The right-hand collection is observable
    /// and has already said what happened to it; refreshing its view as well would throw away the
    /// scroll position for the sake of a single insert.
    /// </summary>
    private void RefreshLeft()
    {
        LocalView?.Refresh();

        Recount();
    }

    private void Recount()
    {
        // The left total is what is left to pick, so queueing a mod takes it out of both halves of
        // the count rather than leaving a total nothing can reach.
        LocalTotal = _local.Count(x => _queued.Contains(x.Mod.Identity) is false);
        LocalCount = _local.Count(PassesLocal);
        UpdateCount = _local.Count(x => _queued.Contains(x.Mod.Identity) is false && IsUpdate(x.Mod));
        UnregisteredCount = _local.Count(x => _queued.Contains(x.Mod.Identity) is false && IsUnregisteredVersionOfKnownMod(x.Mod));

        // Marked here rather than when the row is built, because a row that has changed sides has to
        // drop the chip: on the right it would sit next to "Pending" saying the same thing twice.
        foreach (var row in _local)
        {
            row.Status = _queued.Contains(row.Mod.Identity) is false && IsUpdate(row.Mod)
                ? ModDisplayStatus.UpdateAvailable
                : ModDisplayStatus.None;
        }

        RepoTotal = _repoMods.Count;
        RepoCount = _repoMods.Count(PassesRepo);

        QueuedCount = _repoMods.Count(IsPending);
    }


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
