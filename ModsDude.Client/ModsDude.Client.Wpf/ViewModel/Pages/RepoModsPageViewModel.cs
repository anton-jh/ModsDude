using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The repo's mods: what it holds, searchable, and the few things that can be done to a version that
/// is already registered - reorder a mod's versions, delete a version, delete a mod, and delete a
/// picked set of unused versions in one go.
/// </summary>
/// <remarks>
/// <b>Nothing is imported here.</b> Import and Manage were once one page that showed what the sources
/// held beside what the repo held, and the move between them was the point of it. Importing now
/// happens where a mod is chosen for a profile - the profile's mod list editor registers whatever its
/// draft pins that the repo does not yet hold, as part of the save - so this page has no left list, no
/// sources and no disk scan: it reads the repo and nothing else. See docs/09-mod-catalog.md#manage.
/// </remarks>
public partial class RepoModsPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ModCatalog _catalog;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;
    private readonly ShellNavigationService _shellNavigation;
    private readonly IModsClient _modsClient;
    private readonly IBackgroundTaskReporter _backgroundTasks;

    private readonly CancellationTokenSource _cancellation = new();
    private readonly ModRowActions _rowActions;

    /// <summary>What the repo holds, rebuilt from the catalog.</summary>
    private IReadOnlyList<ModListItemViewModel> _registered = [];


    public RepoModsPageViewModel(
        Repo repo,
        ModCatalog.Factory catalogFactory,
        ModListItemViewModel.Factory itemFactory,
        IModalService modalService,
        IErrorReporter errorReporter,
        ShellNavigationService shellNavigation,
        IModsClient modsClient,
        IBackgroundTaskReporter backgroundTasks)
    {
        _repo = repo;
        _itemFactory = itemFactory;
        _modalService = modalService;
        _errorReporter = errorReporter;
        _shellNavigation = shellNavigation;
        _modsClient = modsClient;
        _backgroundTasks = backgroundTasks;

        // The page owns the catalog and disposes it. No source is ever switched on, so it reads the
        // repo and touches no disk.
        _catalog = catalogFactory.Create(repo);

        // A guest can read the catalog - every GET here is theirs - but everything that writes to the
        // repo needs Member. The commands refuse, and the note is what the refused buttons say.
        CanModify = repo.MembershipLevel >= RepoMembershipLevel.Member;
        ModifyRestriction = CanModify
            ? null
            : "Guests cannot change a repo's mods. Ask an admin for a higher membership level.";

        _rowActions = new ModRowActions(
            ReorderVersionsCommand, DeleteVersionCommand, DeleteModCommand, ModifyRestriction);

        // Enter and a double click do nothing: deleting is the only thing a selection is for here,
        // and it is not something a stray keypress should start.
        Selection = new ModListSelection(
            () => RepoView, () => _registered, _ => { }, "Delete", DescribeDelete, CanPick);

        Selection.Changed += DeleteSelectedCommand.NotifyCanExecuteChanged;

        RepoName = repo.Name;
    }


    public string RepoName { get; }

    /// <summary>
    /// Whether this user may write to the repo's mods. The page itself is open to a guest - browsing
    /// the catalog, searching it and filtering it are all theirs - and only reordering and deleting
    /// are refused.
    /// </summary>
    public bool CanModify { get; }

    /// <summary>Why those are refused, shown on the page. Null where they are not.</summary>
    public string? ModifyRestriction { get; }

    /// <summary>
    /// The versions picked for deletion. Only unused ones can be picked - a delete of anything else
    /// would be refused - so a selection here is always one the server should accept.
    /// </summary>
    public ModListSelection Selection { get; }

    /// <summary>A batch delete is running. It reports to the progress strip, not to the page.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _isDeleting;

    /// <summary>The repo's mods, filtered by the search and the unused toggle.</summary>
    [ObservableProperty]
    private ICollectionView? _repoView;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Narrows the list to what a delete would be accepted for: registered here, and pinned by none
    /// of this repo's profiles.
    /// </summary>
    [ObservableProperty]
    private bool _unusedOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepoCountText))]
    [NotifyPropertyChangedFor(nameof(HasVisibleRepoMods))]
    private int _repoCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepoCountText))]
    [NotifyPropertyChangedFor(nameof(HasRepoMods))]
    private int _repoTotal;


    public bool HasRepoMods => RepoTotal > 0;
    public bool HasVisibleRepoMods => RepoCount > 0;

    public string RepoCountText => Describe(RepoCount, RepoTotal);

    /// <summary>
    /// What the whole repo holds, in the numbers somebody managing it asks for: versions, mods and bytes.
    /// Its own line rather than part of the count beside the search, because the count narrows with the
    /// search and this does not - what a repo costs to keep is not a fact about what is being looked at.
    /// </summary>
    public string StatisticsText => _statistics.Count == 0 ? "" : string.Join(" · ", _statistics);

    public bool HasStatistics => _statistics.Count > 0;

    private IReadOnlyList<string> _statistics = [];


    [RelayCommand]
    private async Task Refresh()
    {
        await ReloadAsync();
    }


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
            await ShowDependentsAsync(
                $"Version '{row.Version}' of '{row.Name}'",
                ct => _modsClient.GetModVersionDependentsV1Async(_repo.Id, row.Id, row.Version, ct),
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
            await ShowDependentsAsync(
                $"'{row.Name}'",
                ct => _modsClient.GetModDependentsV1Async(_repo.Id, row.Id, ct),
                $"A profile depends on '{row.Name}'. Take it out of that profile first.");

            return;
        }

        await ReloadAfterServerChangeAsync();
    }

    /// <summary>
    /// Deletes the picked versions, one call each - except where every version of a mod is picked,
    /// which deletes the mod in one call instead. The server refuses to take a mod's last version on
    /// its own, and a mod with none is not something anything else could represent.
    /// </summary>
    /// <remarks>
    /// Reports to the progress strip rather than the page, and on a token of its own rather than the
    /// page's: the strip outlives the page, so leaving the page is not a reason to stop, and the
    /// strip's Cancel is. Only unused versions can be picked, so a refusal here means a profile took
    /// one up after the list was read - the refused ones are skipped and named at the end.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelected()
    {
        var picked = Selection.Picked().OfType<ModListItemViewModel>().ToList();

        if (picked.Count == 0)
        {
            return;
        }

        var steps = PlanDelete(picked);
        var wholeMods = steps.Count(x => x.WholeMod);

        var confirmation = new ConfirmationDialogViewModel(
            "Really?",
            $"Delete {Versions(picked.Count)} from the repo?\n"
                + (wholeMods == 0 ? "" : $"That is every version of {Mods(wholeMods)}, so {(wholeMods == 1 ? "the mod goes" : "the mods go")} too.\n")
                + "The files go with them, and this cannot be undone.",
            IconKind.Warning,
            DescribeDelete(picked),
            "Keep");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        IsDeleting = true;

        var refused = new List<string>();
        Exception? failure = null;

        using var stop = new CancellationTokenSource();

        try
        {
            using var task = _backgroundTasks.Begin($"Deleting {Versions(picked.Count)} from '{RepoName}'", cancel: stop.Cancel);

            var done = 0;

            foreach (var step in steps)
            {
                task.Report(step.Row.Name, done, picked.Count);

                try
                {
                    if (step.WholeMod)
                    {
                        await _modsClient.DeleteModV1Async(_repo.Id, step.Row.Id, stop.Token);
                    }
                    else
                    {
                        await _modsClient.DeleteModVersionV1Async(_repo.Id, step.Row.Id, step.Row.Version, stop.Token);
                    }
                }
                catch (ApiException<CustomProblemDetails> exception)
                    when (exception.Result.Type is ProblemType.ModInUse or ProblemType.CannotDeleteOnlyModVersion)
                {
                    refused.Add(step.WholeMod ? step.Row.Name : $"{step.Row.Name} {step.Row.Version}");
                }

                done += step.Versions;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the strip. What was deleted stays deleted; the reload below shows it.
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            IsDeleting = false;
        }

        if (_cancellation.IsCancellationRequested)
        {
            // The page is gone, and its catalog with it. The next visit reads the repo afresh.
            if (failure is not null)
            {
                _errorReporter.Record(failure, "deleting mod versions");
            }

            return;
        }

        await ReloadAfterServerChangeAsync();

        if (failure is not null)
        {
            await _errorReporter.ShowAsync(failure, "deleting mod versions");
        }
        else if (refused.Count > 0)
        {
            await ShowRefusal("Some were kept",
                "A profile started using these before they were deleted:\n" + string.Join("\n", refused));
        }
    }

    [RelayCommand]
    private void SelectAllShown() => Selection.SelectAllShown();

    [RelayCommand]
    private void ClearSelection() => Selection.ClearSelection();

    [RelayCommand]
    private void DeselectHidden() => Selection.DeselectHidden();

    private bool CanDeleteSelected() => CanModify && IsDeleting is false && Selection.HasSelection;

    /// <summary>One call a batch delete makes, and how many of the picked versions it takes.</summary>
    private sealed record DeleteStep(ModListItemViewModel Row, int Versions, bool WholeMod);

    private List<DeleteStep> PlanDelete(IReadOnlyList<ModListItemViewModel> picked)
    {
        var held = _registered
            .GroupBy(x => x.Mod.ModId)
            .ToDictionary(x => x.Key, x => x.Count());

        var steps = new List<DeleteStep>();

        foreach (var mod in picked.GroupBy(x => x.Mod.ModId))
        {
            var versions = mod.ToList();

            if (versions.Count == held[mod.Key])
            {
                steps.Add(new DeleteStep(versions[0], versions.Count, WholeMod: true));
            }
            else
            {
                steps.AddRange(versions.Select(x => new DeleteStep(x, 1, WholeMod: false)));
            }
        }

        return steps;
    }

    private static string DescribeDelete(IReadOnlyList<ISelectableRow> picked)
        => $"Delete {Versions(picked.Count)}";

    /// <summary>Only what a delete would be accepted for, and only for somebody allowed to delete.</summary>
    private bool CanPick(ISelectableRow row)
        => CanModify && row is ModListItemViewModel { Mod.IsUnused: true };

    private static string Versions(int count) => count == 1 ? "1 version" : $"{count:N0} versions";

    private static string Mods(int count) => count == 1 ? "1 mod" : $"{count:N0} mods";

    private Task ShowRefusal(string title, string message)
    {
        return _modalService.Show(ConfirmationDialogViewModel.Refusal(title, message));
    }

    /// <summary>
    /// Turns "a profile depends on it" into the profiles and revisions that actually do, each a link
    /// into the history where it can be pruned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked only once the delete has been refused. The server and the foreign key decide; this is
    /// the difference between a wall and a next step, and paying for it before every delete would
    /// mean querying the whole dependency graph to tell somebody nothing was wrong.
    /// </para>
    /// <para>
    /// <paramref name="fallback"/> is the old flat refusal, kept for the case where the follow-up
    /// read fails or comes back empty - a race with somebody else's edit. Being told less is better
    /// than being told nothing after a delete that visibly did not happen.
    /// </para>
    /// </remarks>
    private async Task ShowDependentsAsync(
        string what,
        Func<CancellationToken, Task<ModDependentsDto>> fetch,
        string fallback)
    {
        ModDependentsDto? dependents = null;

        try
        {
            dependents = await fetch(_cancellation.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _errorReporter.Record(exception, "reading what depends on a mod");
        }

        if (dependents is null || dependents.Profiles.Any() is false)
        {
            await ShowRefusal("Still in use", fallback);

            return;
        }

        await _modalService.Show(new ModDependentsModalViewModel(what, dependents, GoToRevisionAsync));
    }

    private Task<bool> GoToRevisionAsync(Guid profileId, int revision)
        => _shellNavigation.GoToProfileHistoryAsync(_repo.Id, profileId, revision);

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
    /// Called when the user navigates away. Stops this page waiting on the catalog.
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
    /// A cancelled load is the expected outcome of navigating away, not something to show the user
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
        _registered = [.. snapshot.Versions
            .Where(x => x.IsOnServer)
            .OrderBy(x => x.Name, NaturalOrder.Comparer)
            // Two different mods can carry one display name, and their sequence numbers say nothing
            // about each other - so the id separates them before either is read.
            .ThenBy(x => x.ModId.Value, StringComparer.Ordinal)
            // The repo's arbitrated order, not one re-derived from the version strings: re-deriving it
            // here would be a second opinion, free to disagree with the one the whole repo shares.
            .ThenBy(x => x.SequenceNumber ?? int.MaxValue)
            .Select(CreateRow)];

        // Rebuilt rather than refreshed, because the list behind it is replaced wholesale - adding a
        // couple of thousand rows to a bound observable collection one at a time is a couple of
        // thousand layout passes.
        var view = CollectionViewSource.GetDefaultView(new ObservableCollection<ModListItemViewModel>(_registered));
        view.Filter = x => x is ModListItemViewModel row && Passes(row);

        RepoView = view;

        _statistics = DescribeRepo(_registered);
        OnPropertyChanged(nameof(StatisticsText));
        OnPropertyChanged(nameof(HasStatistics));

        Recount();

        IsLoading = false;
    }

    /// <summary>The whole repo in a phrase: how many mods, and how many bytes they add up to.</summary>
    private static IReadOnlyList<string> DescribeRepo(IReadOnlyList<ModListItemViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var mods = rows.Select(x => x.Mod.ModId).Distinct().Count();
        // Every row here is registered, and a registered version always has a size.
        var bytes = rows.Sum(x => x.Mod.SizeBytes.GetValueOrDefault());

        return
        [
            rows.Count == 1 ? "1 version" : $"{rows.Count:N0} versions",
            mods == 1 ? "1 mod" : $"{mods:N0} mods",
            $"{ByteSize.Describe(bytes)} in all"
        ];
    }

    private ModListItemViewModel CreateRow(CatalogModVersion version)
    {
        var item = _itemFactory.Create(_repo.Id, version);

        // A registered version has nothing to say about presence, and there is no draft to pick from.
        item.Status = ModDisplayStatus.None;
        item.IsSelectable = CanModify;
        item.IsPickable = CanPick(item);
        item.PickRestriction = CanModify && item.IsPickable is false ? "In use, so it cannot be deleted" : null;
        item.ShowStatistics = true;
        item.Actions = _rowActions;

        // The row's own checkbox writes the flag without going through a gesture.
        item.PropertyChanged += OnRowPropertyChanged;

        return item;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModListItemViewModel.IsSelected))
        {
            Selection.Recount();
        }
    }

    private bool Passes(ModListItemViewModel row)
        => row.Matches(SearchText) && (UnusedOnly is false || row.Mod.IsUnused);

    private static string Describe(int visible, int total)
    {
        var noun = total == 1 ? "version" : "versions";

        return visible == total ? $"{total:N0} {noun}" : $"{visible:N0} of {total:N0} {noun}";
    }

    partial void OnSearchTextChanged(string value)
        => RefreshList();

    partial void OnUnusedOnlyChanged(bool value)
        => RefreshList();

    private void RefreshList()
    {
        RepoView?.Refresh();

        Recount();
    }

    private void Recount()
    {
        RepoTotal = _registered.Count;
        RepoCount = _registered.Count(Passes);

        Selection.Recount();
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoModsPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoModsPageViewModel>(serviceProvider, repo);
    }
}
