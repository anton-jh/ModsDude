using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// Applying a profile to one game: what would change, the confirmation for anything the repo
/// cannot put back, and live progress while it happens.
/// </summary>
public partial class SyncPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly Game _game;
    private readonly ModSyncService _syncService;
    private readonly DriftService _driftService;
    private readonly DriftMonitor _driftMonitor;
    private readonly ProfileService _profileService;
    private readonly ProfileApplyService _applyService;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly IBackgroundTaskReporter _backgroundTasks;

    /// <summary>
    /// One plan per folder the game reaches. Empty until the first load and whenever it failed.
    /// </summary>
    /// <remarks>
    /// The page is about a game, and applying a profile to a game is applying it to each of its
    /// folders - so the preview lists every folder's changes together and Apply executes them in
    /// turn. A game with one folder, which is every game this build's adapters offer, reads exactly
    /// as it always has.
    /// </remarks>
    private IReadOnlyList<ModSyncPlan> _plans = [];
    private ILocalModAdapter? _adapter;

    /// <summary>
    /// The repo's record for every version this profile pins, by identity. It is what lets a plan row
    /// render as the same list row as everywhere else - the real name, the icon, the description -
    /// none of which a dependency carries. Left empty where the fetch failed, which costs the rows
    /// their icons and nothing else.
    /// </summary>
    private IReadOnlyDictionary<ModVersionIdentity, CatalogModVersion> _pinned =
        new Dictionary<ModVersionIdentity, CatalogModVersion>();


    public SyncPageViewModel(
        Repo repo,
        Game game,
        ModSyncService syncService,
        DriftService driftService,
        DriftMonitor driftMonitor,
        ProfileService profileService,
        ProfileApplyService applyService,
        ModListItemViewModel.Factory itemFactory,
        IBackgroundTaskReporter backgroundTasks)
    {
        _backgroundTasks = backgroundTasks;
        _repo = repo;
        _game = game;
        _syncService = syncService;
        _driftService = driftService;
        _driftMonitor = driftMonitor;
        _profileService = profileService;
        _applyService = applyService;
        _itemFactory = itemFactory;

        GameName = game.Name;
        Rows = [];
    }


    public string GameName { get; }

    public ObservableCollection<ModSyncRowViewModel> Rows { get; }


    [ObservableProperty]
    private string _profileName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    private bool _hasPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = "Working out what needs to change...";

    [ObservableProperty]
    private string? _problem;

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _materializationNote = "";

    /// <summary>Only set where a same-disk store silently fell back to copying, which is the case the user did not choose.</summary>
    [ObservableProperty]
    private string? _materializationWarning;

    [ObservableProperty]
    private string? _driftNote;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private double _progressValue;

    public bool CanApply => HasPlan && IsRunning is false;

    /// <summary>
    /// Whether the preview has anything to list. False for a plan of nothing but keeps, where the
    /// summary already says so and an empty card would only leave the user wondering what is missing.
    /// </summary>
    public bool HasChanges => HasPlan && Rows.Count > 0;

    public bool IsIdle => IsRunning is false;
    public bool HasProblem => Problem is not null;


    public void Dispose()
    {
        RefreshCommand.Cancel();
        ApplyCommand.Cancel();
    }


    protected override async Task InitAsync()
    {
        await LoadPlanAsync(CancellationToken.None);
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Refresh(CancellationToken cancellationToken)
    {
        await LoadPlanAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanApply), IncludeCancelCommand = true)]
    private async Task Apply(CancellationToken cancellationToken)
    {
        if (_plans.Count == 0)
        {
            return;
        }

        // Asked once for the game rather than once per folder: it is one disclosure about what is
        // going to the Recycle Bin, and splitting it would be two dialogs saying the same thing.
        if (_plans.Any(x => x.Unrecognised.Count > 0)
            && await _applyService.ConfirmUnrecognisedAsync(_plans) is false)
        {
            return;
        }

        IsRunning = true;
        ProgressValue = 0;
        ProgressText = "Starting...";

        // The one sync that does not go through ProfileApplyService - this page shows the plan and
        // executes it itself - so it announces itself rather than inheriting the announcement.
        using var task = _backgroundTasks.Begin($"Applying '{ProfileName}' to '{GameName}'");

        try
        {
            var progress = ProfileApplyService.Report(task, new Progress<ModSyncProgress>(Report));
            var results = new List<ModSyncResult>();

            // One folder at a time, and each of them gets its turn: a folder that could not be
            // finished must not stop the next one being put right.
            foreach (var plan in _plans)
            {
                results.Add(await _syncService.ExecuteAsync(plan, progress, cancellationToken));
            }

            Status = Describe(results);

            // The plans describe folders that have just changed, so they are stale whatever happened.
            await LoadPlanAsync(CancellationToken.None);

            // And so is the app-level notice, which may be up about exactly this game.
            await _driftMonitor.CheckAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped. Nothing further was changed; re-applying picks up where this left off.";
            await LoadPlanAsync(CancellationToken.None);
        }
        finally
        {
            IsRunning = false;
            ProgressText = "";
            ProgressValue = 0;
        }
    }


    /// <summary>
    /// Everything the plan needs, or the reason there is no plan to make. A dangling active profile
    /// is one of those reasons rather than a failure: the profile was deleted or the user was removed
    /// from its repo, and the answer is to pick another on Manage.
    /// </summary>
    private async Task LoadPlanAsync(CancellationToken cancellationToken)
    {
        _plans = [];
        HasPlan = false;
        Problem = null;

        // The lists are bound, so they are only ever touched on the UI thread - the plan itself is
        // worked out off it, since it hashes files and can talk to the server.
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Rows.Clear();
            OnPropertyChanged(nameof(HasChanges));
        });

        if (_game.ActiveProfile is not ActiveProfile active)
        {
            Fail("No profile is set on this game yet. Pick one on Manage, and this page will say what applying it would do.");

            return;
        }

        if (active.RepoId != _repo.Id)
        {
            Fail("This game follows a profile in another repo. Open that repo to apply it, or pick a profile from this one on Manage.");

            return;
        }

        if (_profileService.Profiles.FirstOrDefault(x => x.Id == active.ProfileId) is not ProfileDto profile)
        {
            ShowDrift(DriftReport.For(DriftStatus.DanglingProfile));
            Fail("The profile this game follows no longer exists, or is no longer visible to you. Pick another on Manage.");

            return;
        }

        ProfileName = profile.Name;

        _adapter ??= _game.GetAdapter(_repo.Adapter)
            .GetLocalCapabilityAdapterFactory<ILocalModAdapter>()
            ?.Invoke();

        if (_adapter is not ILocalModAdapter adapter)
        {
            Fail("This game adapter cannot manage mod folders, so there is nothing to apply.");

            return;
        }

        if (adapter.ModTargets.Count == 0)
        {
            Fail("This game's settings point at no mod folder, so there is nothing to apply. Fill one in on its settings page.");

            return;
        }

        Status = "Working out what needs to change...";

        try
        {
            var plans = new List<ModSyncPlan>();

            // One plan per folder. Nothing is caught per folder here, unlike the one-click apply: this
            // page exists to show the plan before anything runs, and a preview missing a third of
            // what is about to happen would be worse than the sentence saying it could not be made.
            foreach (var target in adapter.ModTargets)
            {
                plans.Add(await _syncService.PlanAsync(
                    new ModSyncRequest(_game.Identity, target, adapter, _repo.Id, active.ProfileId)
                    {
                        ProfileName = profile.Name
                    },
                    cancellationToken));
            }

            _pinned = await LoadPinnedAsync(active.ProfileId, cancellationToken);

            await Application.Current.Dispatcher.InvokeAsync(() => Publish(plans));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UserFriendlyException exception)
        {
            Fail(exception.DeveloperMessage ?? exception.Message);
        }
        catch (Exception exception)
        {
            Fail($"The plan could not be worked out: {exception.Message}");
        }
    }

    /// <summary>
    /// The repo's record for what the profile pins, for the rows to render from. Never fatal: the
    /// plan is already worked out by this point, and a row with no record still says what will happen
    /// to the file - it just says it without an icon.
    /// </summary>
    private async Task<IReadOnlyDictionary<ModVersionIdentity, CatalogModVersion>> LoadPinnedAsync(
        Guid profileId, CancellationToken cancellationToken)
    {
        try
        {
            var pinned = await _profileService.GetPinnedMods(_repo.Id, profileId, null, cancellationToken);

            return pinned.ToDictionary(x => x.Version.Identity, x => x.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new Dictionary<ModVersionIdentity, CatalogModVersion>();
        }
    }

    private void Publish(IReadOnlyList<ModSyncPlan> plans)
    {
        _plans = plans;
        HasPlan = true;

        // Keeps are left out on purpose: they are what the summary counts, and on a re-apply they are
        // nearly the whole list. A preview headed "What would change" listing what would not is the
        // fastest way to hide the two lines that matter.
        var changing = plans
            .SelectMany(x => x.Items)
            .Where(x => x.Action is not ModSyncAction.Keep)
            .OrderBy(x => x.Action)
            .ThenBy(x => x.DisplayName, NaturalOrder.Comparer);

        foreach (var item in changing)
        {
            Rows.Add(new ModSyncRowViewModel(item, CreateItem(item)));
        }

        OnPropertyChanged(nameof(HasChanges));

        var hasWork = plans.Any(x => x.HasWork);
        var keeps = plans.Sum(x => x.KeepCount);

        Summary = hasWork
            ? $"{plans.Sum(x => x.InstallCount)} to install, {plans.Sum(x => x.ReplaceCount)} to replace, " +
              $"{plans.Sum(x => x.UninstallCount)} to uninstall, {plans.Sum(x => x.QuarantineCount)} to move to the Recycle Bin, " +
              $"{plans.Sum(x => x.RenameCount)} to rename, {keeps} already correct."
            : $"Nothing to do - all {keeps} mods already match this profile.";

        Status = hasWork
            ? $"{plans.Sum(x => x.HashesToFetch.Count)} mods have to be fetched before anything is changed."
            : "This game already matches its profile.";

        // Off the folders rather than the folder, and said once per distinct answer: two folders on
        // one disk have one story to tell and two on different disks have two.
        MaterializationNote = string.Join(' ', plans.Select(DescribeMaterialization).Distinct());

        MaterializationWarning = plans.Any(x => x.Materialization.FellBackToCopy)
            ? "The store on this disk cannot hardlink into this mod folder - exFAT and network paths cannot - so every " +
              "install is a full copy even though the store is on the same disk."
            : null;

        // One line per folder, named where there is more than one: which folder is out of step is the
        // half worth knowing once a game reaches several.
        ShowDrift([.. plans.Select(plan => (
            plan.Target.DisplayName,
            Report: _driftService.Check(plan.TargetRef, _game.ActiveProfile, plan.ModFolder)))]);
    }

    private static string DescribeMaterialization(ModSyncPlan plan)
    {
        return plan.Materialization.Method is MaterializationMethod.Hardlink
            ? "Mods are hardlinked from the store on this disk, so installing costs no extra space and takes seconds."
            : $"Mods are copied into the mod folder from the store at {plan.ServingStore.RootPath}.";
    }

    /// <summary>
    /// The shared list row for one plan item, so a mod about to be installed looks exactly as it does
    /// on the repo's mod list and on the profile's.
    /// </summary>
    /// <remarks>
    /// A pinned version is registered, so the repo's record answers everything the row renders. The
    /// rest of the list is files the profile does not pin - an uninstall, an unrecognised file heading
    /// for the Recycle Bin - which no catalog record describes at all, and those fall back to what the
    /// adapter read off the file itself: a local-only version, which renders as initials and asks the
    /// server for nothing.
    /// </remarks>
    private ModListItemViewModel CreateItem(ModSyncItem item)
    {
        var version = item.DesiredVersion is ModVersionKey desired
            && _pinned.TryGetValue(new ModVersionIdentity(item.ModId, desired), out var registered)
            ? registered
            : new CatalogModVersion(
                item.ModId,
                // A file that blocks an install is named rather than versioned, so there is no version
                // to show and the row's chip stays empty.
                item.DesiredVersion ?? item.InstalledVersion ?? default,
                item.DisplayName,
                string.Empty,
                IsLocal: true,
                IsOnServer: false,
                Locked: item.Locked);

        return _itemFactory.Create(_repo.Id, version);
    }

    private void ShowDrift(IReadOnlyList<(string? Folder, DriftReport Report)> reports)
    {
        var lines = reports
            .Select(x => (x.Folder, Note: Describe(x.Report)))
            .Where(x => x.Note is not null)
            .Select(x => reports.Count > 1 && x.Folder is string folder ? $"{folder}: {x.Note}" : x.Note)
            .ToList();

        DriftNote = lines.Count > 0 ? string.Join('\n', lines) : null;
    }

    private void ShowDrift(DriftReport report) => ShowDrift([(null, report)]);

    private static string? Describe(DriftReport report)
    {
        return report.Status switch
        {
            DriftStatus.Drifted =>
                $"{report.DifferenceCount} files differ from what was last applied here. Mods updated inside the game look like this.",
            DriftStatus.NeverSynced => "This profile has not been applied to this game yet.",
            DriftStatus.NotApplied => report.AppliedProfileName is string applied
                ? $"The mod folder is still on '{applied}'. This profile has not been applied here yet."
                : "The mod folder is still on the profile it was last applied to, not this one.",
            DriftStatus.FolderRepointed => "The mod folder has been pointed somewhere else, and nothing has been applied there yet.",
            DriftStatus.DanglingProfile => "The profile this game follows is gone.",
            DriftStatus.FolderUnreachable => "The mod folder cannot be reached right now, so nothing is known about it.",
            DriftStatus.InSync => "The mod folder still matches what was last applied here.",
            _ => null
        };
    }

    private void Report(ModSyncProgress progress)
    {
        var phase = progress.Phase switch
        {
            ModSyncPhase.Fetching => "Fetching",
            ModSyncPhase.Removing => "Removing",
            ModSyncPhase.Installing => "Installing",
            _ => "Finishing"
        };

        ProgressText = progress.Detail is null
            ? $"{phase} {progress.Completed} of {progress.Total}"
            : $"{phase} {progress.Completed + 1} of {progress.Total}: {progress.Detail}";

        ProgressValue = progress.Total == 0 ? 0 : progress.Completed * 100d / progress.Total;
    }

    /// <summary>
    /// What happened across every folder, in one line. Counts are summed: what the user asked for
    /// was one apply, and the plan they read was the folders' changes listed together.
    /// </summary>
    private static string Describe(IReadOnlyList<ModSyncResult> results)
    {
        var failures = results.SelectMany(x => x.Failures).ToList();

        if (results.Any(x => x.Completed is false))
        {
            var first = failures.FirstOrDefault();

            return failures.Count == 1 && first is not null
                ? $"One mod could not be applied ({first.ModId}): {first.Message}"
                : $"{failures.Count} mods could not be applied. The game is left as it is until they are.";
        }

        var quarantined = results.SelectMany(x => x.Quarantined).ToList();
        var recycled = quarantined.Count(x => x.Destination is QuarantineDestination.RecycleBin);
        var moved = quarantined.Count(x => x.Destination is QuarantineDestination.QuarantineFolder);
        var stuck = quarantined.Count(x => x.Destination is QuarantineDestination.Failed);

        var notes = new List<string> { "The mod folder now matches the profile." };

        if (recycled > 0) notes.Add($"{recycled} unrecognised files are in the Recycle Bin.");
        if (moved > 0) notes.Add($"{moved} could not be recycled and were moved into the store's quarantine folder instead.");
        if (stuck > 0) notes.Add($"{stuck} could not be moved at all and are still where they were.");

        return string.Join(' ', notes);
    }

    private void Fail(string message)
    {
        Problem = message;
        Status = "";
        Summary = "";
        MaterializationNote = "";
        MaterializationWarning = null;

        OnPropertyChanged(nameof(HasProblem));
    }

    partial void OnProblemChanged(string? value)
    {
        OnPropertyChanged(nameof(HasProblem));
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public SyncPageViewModel Create(Repo repo, Game game)
            => ActivatorUtilities.CreateInstance<SyncPageViewModel>(serviceProvider, repo, game);
    }
}
