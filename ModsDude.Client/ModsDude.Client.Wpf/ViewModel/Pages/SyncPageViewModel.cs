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
/// Applying a profile to one instance: what would change, the confirmation for anything the repo
/// cannot put back, and live progress while it happens.
/// </summary>
public partial class SyncPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly LocalInstance _instance;
    private readonly ModSyncService _syncService;
    private readonly InstanceDriftService _driftService;
    private readonly InstanceDriftMonitor _driftMonitor;
    private readonly ProfileService _profileService;
    private readonly ProfileApplyService _applyService;
    private readonly ModListItemViewModel.Factory _itemFactory;

    private ModSyncPlan? _plan;
    private IInstanceModAdapter? _adapter;

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
        LocalInstance instance,
        ModSyncService syncService,
        InstanceDriftService driftService,
        InstanceDriftMonitor driftMonitor,
        ProfileService profileService,
        ProfileApplyService applyService,
        ModListItemViewModel.Factory itemFactory)
    {
        _repo = repo;
        _instance = instance;
        _syncService = syncService;
        _driftService = driftService;
        _driftMonitor = driftMonitor;
        _profileService = profileService;
        _applyService = applyService;
        _itemFactory = itemFactory;

        InstanceName = instance.Name;
        Rows = [];
    }


    public string InstanceName { get; }

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
        if (_plan is not ModSyncPlan plan)
        {
            return;
        }

        if (plan.Unrecognised.Count > 0 && await _applyService.ConfirmUnrecognisedAsync(plan) is false)
        {
            return;
        }

        IsRunning = true;
        ProgressValue = 0;
        ProgressText = "Starting...";

        try
        {
            var progress = new Progress<ModSyncProgress>(Report);
            var result = await _syncService.ExecuteAsync(plan, progress, cancellationToken);

            Status = Describe(result);

            // The plan describes a folder that has just changed, so it is stale whatever happened.
            await LoadPlanAsync(CancellationToken.None);

            // And so is the app-level notice, which may be up about exactly this instance.
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
        _plan = null;
        HasPlan = false;
        Problem = null;

        // The lists are bound, so they are only ever touched on the UI thread - the plan itself is
        // worked out off it, since it hashes files and can talk to the server.
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Rows.Clear();
            OnPropertyChanged(nameof(HasChanges));
        });

        if (_instance.ActiveProfile is not ActiveProfile active)
        {
            Fail("No profile is set on this instance yet. Pick one on Manage, and this page will say what applying it would do.");

            return;
        }

        if (active.RepoId != _repo.Id)
        {
            Fail("This instance follows a profile in another repo. Open that repo to apply it, or pick a profile from this one on Manage.");

            return;
        }

        if (_profileService.Profiles.FirstOrDefault(x => x.Id == active.ProfileId) is not ProfileDto profile)
        {
            ShowDrift(InstanceDriftReport.For(InstanceDriftStatus.DanglingProfile));
            Fail("The profile this instance follows no longer exists, or is no longer visible to you. Pick another on Manage.");

            return;
        }

        ProfileName = profile.Name;

        _adapter ??= _instance.GetAdapter(_repo.Adapter)
            .GetInstanceCapabilityAdapterFactory<IInstanceModAdapter>()
            ?.Invoke();

        if (_adapter is not IInstanceModAdapter adapter)
        {
            Fail("This game adapter cannot manage mod folders, so there is nothing to apply.");

            return;
        }

        Status = "Working out what needs to change...";

        try
        {
            var plan = await _syncService.PlanAsync(
                new ModSyncRequest(_instance.Id, adapter, _repo.Id, active.ProfileId) { ProfileName = profile.Name },
                cancellationToken);

            _pinned = await LoadPinnedAsync(active.ProfileId, cancellationToken);

            await Application.Current.Dispatcher.InvokeAsync(() => Publish(plan));
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

    private void Publish(ModSyncPlan plan)
    {
        _plan = plan;
        HasPlan = true;

        // Keeps are left out on purpose: they are what the summary counts, and on a re-apply they are
        // nearly the whole list. A preview headed "What would change" listing what would not is the
        // fastest way to hide the two lines that matter.
        var changing = plan.Items
            .Where(x => x.Action is not ModSyncAction.Keep)
            .OrderBy(x => x.Action)
            .ThenBy(x => x.DisplayName, NaturalOrder.Comparer);

        foreach (var item in changing)
        {
            Rows.Add(new ModSyncRowViewModel(item, CreateItem(item)));
        }

        OnPropertyChanged(nameof(HasChanges));

        Summary = plan.HasWork
            ? $"{plan.InstallCount} to install, {plan.ReplaceCount} to replace, " +
              $"{plan.UninstallCount} to uninstall, {plan.QuarantineCount} to move to the Recycle Bin, " +
              $"{plan.RenameCount} to rename, {plan.KeepCount} already correct."
            : $"Nothing to do - all {plan.KeepCount} mods already match this profile.";

        Status = plan.HasWork
            ? $"{plan.HashesToFetch.Count} mods have to be fetched before anything is changed."
            : "This instance already matches its profile.";

        MaterializationNote = plan.Materialization.Method is MaterializationMethod.Hardlink
            ? "Mods are hardlinked from the store on this disk, so installing costs no extra space and takes seconds."
            : $"Mods are copied into the mod folder from the store at {plan.ServingStore.RootPath}.";

        MaterializationWarning = plan.Materialization.FellBackToCopy
            ? "The store on this disk cannot hardlink into this mod folder - exFAT and network paths cannot - so every " +
              "install is a full copy even though the store is on the same disk."
            : null;

        ShowDrift(_driftService.Check(_instance.Id, _instance.ActiveProfile, plan.ModFolder));
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

    private void ShowDrift(InstanceDriftReport report)
    {
        DriftNote = report.Status switch
        {
            InstanceDriftStatus.Drifted =>
                $"{report.DifferenceCount} files differ from what was last applied here. Mods updated inside the game look like this.",
            InstanceDriftStatus.NeverSynced => "This profile has not been applied to this instance yet.",
            InstanceDriftStatus.DanglingProfile => "The profile this instance follows is gone.",
            InstanceDriftStatus.FolderUnreachable => "The mod folder cannot be reached right now, so nothing is known about it.",
            InstanceDriftStatus.InSync => "The mod folder still matches what was last applied here.",
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

    private static string Describe(ModSyncResult result)
    {
        if (result.Completed is false)
        {
            var first = result.Failures.FirstOrDefault();

            return result.Failures.Count == 1 && first is not null
                ? $"One mod could not be applied ({first.ModId}): {first.Message}"
                : $"{result.Failures.Count} mods could not be applied. The instance is left as it is until they are.";
        }

        var recycled = result.Quarantined.Count(x => x.Destination is QuarantineDestination.RecycleBin);
        var moved = result.Quarantined.Count(x => x.Destination is QuarantineDestination.QuarantineFolder);
        var stuck = result.Quarantined.Count(x => x.Destination is QuarantineDestination.Failed);

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
        public SyncPageViewModel Create(Repo repo, LocalInstance instance)
            => ActivatorUtilities.CreateInstance<SyncPageViewModel>(serviceProvider, repo, instance);
    }
}
