using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// Machine-wide settings: which store serves each disk holding mod folders, where those stores live
/// and how large they may grow, the one image cache that serves the whole machine - and what all of
/// that is currently costing in disk, with the means to take it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Housekeeping acts on what is saved, not on what is typed</b>, so the buttons that verify and
/// reclaim are disabled while there are unsaved edits. Otherwise "reclaim this store" would mean the
/// folder in the text box on a page where that folder has not been written down anywhere yet, which
/// is a good way to clear the wrong directory.
/// </para>
/// <para>
/// <b>Staying inside the size limit is not on this page</b>, because it is not the user's job - see
/// <see cref="ContentStoreMaintenance.SweepAllAsync"/>, which this page triggers on save and which
/// startup and every import trigger too. What is left here is the two things automation cannot
/// decide: whether the bytes are still good, and whether you want the space back now.
/// </para>
/// <para>
/// Measuring walks a store's whole blob tree and reads a link count per file, so it happens off the
/// UI thread and the rows say so until it lands.
/// </para>
/// </remarks>
public partial class SettingsPageViewModel
    : PageViewModel, IDisposable
{
    private const long _bytesPerGigabyte = 1024L * 1024 * 1024;

    private readonly ClientSettingsRepository _settingsRepository;
    private readonly ContentStoreMaintenance _maintenance;
    private readonly ModImageCache _imageCache;
    private readonly NavigationLockService _navigationLockService;
    private readonly IModalService _modalService;
    private readonly IDialogService _dialogService;
    private readonly IBackgroundTaskReporter _backgroundTasks;
    private readonly Dictionary<string, ContentStoreViewModel> _storesByVolume = [];

    /// <summary>
    /// The stores as they are actually configured on disk, keyed by volume, refreshed whenever they
    /// are measured. What a row displays and what a row's buttons act on are deliberately two
    /// different things - see the remarks on this class.
    /// </summary>
    private IReadOnlyDictionary<string, ContentStore> _configuredStores =
        new Dictionary<string, ContentStore>(StringComparer.OrdinalIgnoreCase);


    public SettingsPageViewModel(
        ClientSettingsRepository settingsRepository,
        GameRepository gameRepository,
        ContentStoreMaintenance maintenance,
        ModImageCache imageCache,
        IDialogService dialogService,
        IModalService modalService,
        NavigationLockService navigationLockService,
        IBackgroundTaskReporter backgroundTasks)
    {
        _settingsRepository = settingsRepository;
        _maintenance = maintenance;
        _imageCache = imageCache;
        _dialogService = dialogService;
        _modalService = modalService;
        _navigationLockService = navigationLockService;
        _backgroundTasks = backgroundTasks;

        var settings = settingsRepository.Settings;

        // A store on a disk with no mod folders on it serves nothing, so the disks with games on
        // them are what the page is about.
        var modFolderVolumes = gameRepository.Games
            .SelectMany(x => x.Targets)
            .Select(x => x.ModFolder)
            .GroupBy(FileSystemHelper.NormalizeVolumeRoot)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidateVolumes = GetCandidateVolumes(modFolderVolumes.Select(x => x.Key), settings);

        ModFolderVolumes = [];
        Stores = [];

        ImageCache = new ImageCacheViewModel(
            settings.ImageCache.Path,
            settings.ImageCache.MaxSizeBytes / (double)_bytesPerGigabyte,
            dialogService);
        ImageCache.Modified += OnStoreModified;

        foreach (var volume in modFolderVolumes)
        {
            var row = new VolumeAssignmentViewModel(
                volume.Key,
                volume.Count(),
                settings.GetServingVolume(volume.Key),
                candidateVolumes);

            row.ServingVolumeChanged += OnServingVolumeChanged;
            ModFolderVolumes.Add(row);
        }

        RefreshStores();
    }


    public ObservableCollection<VolumeAssignmentViewModel> ModFolderVolumes { get; }
    public ObservableCollection<ContentStoreViewModel> Stores { get; }
    public ImageCacheViewModel ImageCache { get; }

    public bool HasVolumes => ModFolderVolumes.Count > 0;
    public bool HasNoVolumes => ModFolderVolumes.Count == 0;
    public bool HasStores => Stores.Count > 0;

    /// <summary>
    /// Whether the housekeeping buttons are live. Off while something is running, and off while
    /// there are unsaved edits - see the remarks on this class for why the second one matters.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    [NotifyPropertyChangedFor(nameof(ManagementBlockedReason))]
    [NotifyCanExecuteChangedFor(nameof(VerifyStoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReclaimStoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyQuarantineCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyImageCacheCommand))]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    [NotifyCanExecuteChangedFor(nameof(VerifyStoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReclaimStoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyQuarantineCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyImageCacheCommand))]
    private bool _isBusy;

    public bool CanManage => HasUnsavedChanges is false && IsBusy is false;

    public string ManagementBlockedReason => HasUnsavedChanges
        ? "Save your changes to verify or reclaim a store - these act on the folders as they are saved."
        : string.Empty;


    [RelayCommand]
    public async Task SaveChanges()
    {
        var errors = GetValidationErrors();

        if (errors.Count > 0)
        {
            var modal = ConfirmationDialogViewModel.ValidationErrors(errors);
            await _modalService.Show(modal);

            return;
        }

        var settings = _settingsRepository.Settings;

        foreach (var volume in ModFolderVolumes)
        {
            settings.StoreAssignments[volume.VolumeRoot] = volume.ServingVolume;
        }

        // Entries for volumes that no longer serve anything are left alone: they cost nothing, and
        // dropping them would throw away a size the user set on a disk they are between uses of.
        foreach (var store in Stores)
        {
            settings.Stores[store.VolumeRoot] = new ContentStoreSettings()
            {
                Path = store.Path,
                MaxSizeBytes = (long)(store.MaxSizeGigabytes * _bytesPerGigabyte)
            };
        }

        settings.ImageCache.Path = ImageCache.Path;
        settings.ImageCache.MaxSizeBytes = (long)(ImageCache.MaxSizeGigabytes * _bytesPerGigabyte);

        _settingsRepository.Save();
        _navigationLockService.ReleaseLock(this);

        HasUnsavedChanges = false;

        // A limit that has just come down is the third way a store ends up over it, and the only one
        // with a person watching. Before the measure, so what the rows then report is the size after
        // the trim rather than a number that shrinks a second later on its own.
        await _maintenance.SweepAllAsync(CancellationToken.None);

        // The stores may now be somewhere else or allowed to be a different size, so what was
        // measured a moment ago is about a different set of folders.
        await RefreshUsageAsync();
    }

    // There was a "Sweep to limit" button here. Keeping a store inside a limit the user typed is the
    // app's promise to keep, not a chore to hand back to them - and a button is only pressed by the
    // people who notice it. ContentStoreMaintenance.SweepAllAsync does it now, on the three events
    // that can put a store over: startup, a finished import, and this page being saved with a smaller
    // number in it.

    /// <summary>
    /// Reads every file in a store and drops the ones that are no longer what their name says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exhaustive counterpart to the check that rides along with drift, which only ever looks at
    /// files a mod folder reported changed. This one catches what nothing was watching: a failing
    /// disk, or a tool that rewrote a blob without going near a mod folder.
    /// </para>
    /// <para>
    /// Asked about first, and not because it is dangerous - it is the safest button on the page,
    /// since everything it can remove is re-downloadable - but because it is <em>slow</em>. It reads
    /// every byte in the store, which on a full one is tens of gigabytes, and a button that silently
    /// pins the page for six minutes is a button people press twice.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanManage))]
    public async Task VerifyStore(ContentStoreViewModel row)
    {
        if (FindStore(row) is not ContentStore store)
        {
            return;
        }

        var size = row.Usage is ContentStoreUsage usage && usage.TotalBytes > 0
            ? $"about {ByteSize.Describe(usage.TotalBytes)} across {usage.Entries} files"
            : "every file in it";

        var confirmation = new ConfirmationDialogViewModel(
            $"Check the store on {row.VolumeRoot} for damage?",
            $"Every mod file is read back and checked against what it is filed as - {size}, so this takes a "
                + "while and works the disk. Progress shows at the top of the window, and you can stop it "
                + "there; what it has already checked still counts."
                + "\n\nAnything that fails is dropped and downloads again when a profile needs it.",
            IconKind.Question,
            "Check it",
            "Not now");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();

        // The pass that most needs the strip: it reads every byte in the store, holding the store to
        // itself while it does. Its Stop is the strip's Cancel, so stopping a six-minute pass does not
        // depend on staying on the page that started it.
        using var task = Announce($"Checking the store on {row.VolumeRoot}", cancellation.Cancel);

        // No staleness guard needed any more: the handle is this run's, and a report arriving after it
        // is disposed is written to a task the strip no longer lists.
        var progress = new Progress<ContentStoreVerificationProgress>(x => task.Report(
            $"{x.Checked} of {x.Total} files, {ByteSize.Describe(x.BytesRead)} of {ByteSize.Describe(x.TotalBytes)}",
            x.Checked,
            x.Total));

        try
        {
            var report = await RunCancellableAsync(
                () => _maintenance.VerifyAsync(store, progress, cancellation.Token, Waiting(task)));

            await ReportVerificationAsync(row, report);
        }
        catch (OperationCanceledException)
        {
            // Not a failure. A partial pass checked real files and removed anything it found, so it
            // is reported rather than swallowed - and the rest is one more press away.
            await ReportAsync(
                "Stopped",
                "The check was stopped part way. Anything it found before that was already dealt with, "
                    + "and running it again starts from the top.");
        }

        await RefreshUsageAsync();
    }

    /// <summary>
    /// What a finished pass found, and - the part that matters - where to go next.
    /// </summary>
    /// <remarks>
    /// Dropping a bad blob repairs the store and <b>not</b> the mod folders hardlinked to it, which
    /// are still holding the same wrong bytes under the same names. A report that said only "removed
    /// 2 files" would read as done when it is half done, so the folders that need re-applying are
    /// named.
    /// </remarks>
    private async Task ReportVerificationAsync(ContentStoreViewModel row, StoreVerificationReport? report)
    {
        if (report is null)
        {
            return;
        }

        var result = report.Result;

        if (result.FoundProblems is false)
        {
            await ReportAsync(
                "All good",
                $"Read {result.Checked} files on {row.VolumeRoot} and every one of them is still what it is filed as."
                    + (result.Unreadable > 0
                        ? $"\n\n{result.Unreadable} could not be read - something has them open - and are worth another pass later."
                        : string.Empty));

            return;
        }

        var kept = result.Corrupt.Count - result.Removed;

        var affected = report.Affected.Count > 0
            ? "\n\nThese mod folders are still running the bad files, and dropping the cached copy does not fix them - "
                + "re-apply each one:\n"
                + string.Join('\n', report.Affected.Select(x =>
                    $"  • {x.ModFolder}{(x.ProfileName is string name ? $" ({name})" : "")} - {x.Mods} mod{(x.Mods == 1 ? "" : "s")}"))
            : "\n\nNothing on this machine is running them, so dropping them is the whole repair.";

        await ReportAsync(
            "Found damage",
            $"{result.Corrupt.Count} of {result.Checked} files no longer match what they are filed as. "
                + $"{result.Removed} were dropped and download again on demand."
                + (kept > 0 ? $" {kept} could not be dropped because something has them open." : string.Empty)
                + affected);
    }

    /// <summary>
    /// Gives back the space a store is costing, keeping what the mod folders it serves are running.
    /// </summary>
    /// <remarks>
    /// Safe to offer, because everything it drops is registered somewhere and therefore
    /// re-downloadable - the cost is bandwidth, never data. And it is not "empty the store": a file
    /// the mod folder on this disk already holds costs the store nothing, so dropping it would free
    /// nothing and buy a re-download. See <see cref="ContentStore.Reclaim"/>.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanManage))]
    public async Task ReclaimStore(ContentStoreViewModel row)
    {
        if (FindStore(row) is not ContentStore store)
        {
            return;
        }

        var amount = row.Usage is { ReclaimableBytes: > 0 } usage
            ? ByteSize.Describe(usage.ReclaimableBytes)
            : "the space this store is using";

        var confirmation = new ConfirmationDialogViewModel(
            $"Reclaim {amount} from {row.VolumeRoot}?",
            "Every mod file kept here that is not already installed in a mod folder is dropped. Nothing is "
                + "lost - each one is registered in a repo and downloads again when a profile needs it - but "
                + "the next sync to a disk this store serves will have to fetch what it needs."
                + "\n\nWhat your installed profiles are running stays where it is, here and in the mod folder.",
            IconKind.Question,
            "Reclaim it",
            "Leave it");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        using var task = Announce($"Reclaiming space from the store on {row.VolumeRoot}");

        var result = await RunAsync(() => _maintenance.ReclaimAsync(store, CancellationToken.None, Waiting(task)));

        if (result is null)
        {
            return;
        }

        await ReportAsync(
            "Reclaimed",
            $"Dropped {result.EntriesDeleted} files and freed {ByteSize.Describe(result.BytesReclaimed)}."
                + (result.Failed > 0
                    ? $"\n\n{result.Failed} could not be removed because something else is holding them open. They go on the next tidy-up."
                    : string.Empty));

        await RefreshUsageAsync();
    }

    /// <summary>
    /// Deletes the files sync rescued into this store because the Recycle Bin would not take them.
    /// </summary>
    /// <remarks>
    /// The one destructive button on this page, and asked about as such: a quarantined file is
    /// precisely a mod that <em>no</em> repo registers, which is why it was moved rather than deleted.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanManage))]
    public async Task EmptyQuarantine(ContentStoreViewModel row)
    {
        if (FindStore(row) is not ContentStore store)
        {
            return;
        }

        var confirmation = new ConfirmationDialogViewModel(
            "Delete the rescued files?",
            $"These are files sync found in a mod folder that no repo has registered, kept in {store.QuarantinePath} "
                + "because the Recycle Bin would not take them. Nothing can fetch them back.\n\nThis cannot be undone!",
            IconKind.Warning,
            "Delete them",
            "Keep them");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        using var task = Announce($"Emptying the quarantine folder on {row.VolumeRoot}");

        var reclaimed = await RunAsync(() => _maintenance.ClearQuarantineAsync(store, CancellationToken.None, Waiting(task)));

        if (reclaimed is null)
        {
            return;
        }

        await ReportAsync("Deleted", $"Freed {ByteSize.Describe(reclaimed.Value)}.");
        await RefreshUsageAsync();
    }

    /// <summary>
    /// Empties the machine's image cache. Costs re-fetching thumbnails and nothing else, so it is
    /// the one of these that does not ask first.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanManage))]
    public async Task EmptyImageCache()
    {
        var reclaimed = await RunAsync(_imageCache.Clear);

        if (reclaimed is null)
        {
            return;
        }

        await ReportAsync(
            "Emptied",
            $"Freed {ByteSize.Describe(reclaimed.Value)}. Mod artwork is fetched again as lists are drawn.");

        await RefreshUsageAsync();
    }

    public void Dispose()
    {
        foreach (var volume in ModFolderVolumes)
        {
            volume.ServingVolumeChanged -= OnServingVolumeChanged;
        }

        ImageCache.Modified -= OnStoreModified;

        _navigationLockService.ReleaseLock(this);
    }


    protected override Task InitAsync()
    {
        return RefreshUsageAsync();
    }


    /// <summary>
    /// Counts what every store and the image cache are holding, off the UI thread.
    /// </summary>
    /// <remarks>
    /// A store is measured by walking its whole blob tree and reading a link count per file, which
    /// on a full one is tens of thousands of handles. WPF marshals the property changes back to the
    /// dispatcher itself, so nothing here dispatches.
    /// </remarks>
    private async Task RefreshUsageAsync()
    {
        var rows = Stores.ToList();
        var imageCache = ImageCache;

        await Task.Run(() =>
        {
            var stores = _maintenance.GetStores();

            _configuredStores = stores.ToDictionary(x => x.VolumeRoot, StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                // A row with no store behind it is one whose volume was only just chosen and never
                // saved. Reporting it as empty is true: there is no folder yet.
                row.Usage = _configuredStores.TryGetValue(row.VolumeRoot, out var store)
                    ? store.Measure()
                    : ContentStoreUsage.Empty;
            }

            imageCache.Usage = _imageCache.Measure();
        });
    }

    /// <summary>The store a row's buttons act on: the saved one, never the one being typed.</summary>
    private ContentStore? FindStore(ContentStoreViewModel row)
    {
        return _configuredStores.GetValueOrDefault(row.VolumeRoot);
    }

    /// <summary>
    /// Runs one piece of housekeeping off the UI thread with the buttons held down, and turns a
    /// failure into a dialog rather than the app's error modal - a store that could not be swept is
    /// a full disk, not a broken client.
    /// </summary>
    private async Task<T?> RunAsync<T>(Func<T> work)
        where T : class
    {
        IsBusy = true;

        try
        {
            return await Task.Run(work);
        }
        catch (Exception exception)
        {
            await _modalService.Show(ConfirmationDialogViewModel.Refusal("That did not work", exception.Message));

            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc cref="RunAsync{T}(Func{T})"/>
    /// <remarks>
    /// The asynchronous form, and the one that lets a cancellation straight through: stopping a
    /// verification pass on purpose is not a failure, and turning it into "that did not work" would
    /// be the app calling the user's own decision an error.
    /// </remarks>
    private async Task<T?> RunCancellableAsync<T>(Func<Task<T>> work)
        where T : class
    {
        IsBusy = true;

        try
        {
            return await Task.Run(work);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await _modalService.Show(ConfirmationDialogViewModel.Refusal("That did not work", exception.Message));

            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc cref="RunAsync{T}(Func{T})"/>
    /// <remarks>
    /// For the store housekeeping, which is asynchronous now that it waits for the store to itself
    /// before touching it - a sweep that deletes blobs out from under a sync that is linking them is
    /// the one way this page could make things worse than it found them.
    /// </remarks>
    private async Task<T?> RunAsync<T>(Func<Task<T>> work)
        where T : class
    {
        IsBusy = true;

        try
        {
            return await Task.Run(work);
        }
        catch (Exception exception)
        {
            await _modalService.Show(ConfirmationDialogViewModel.Refusal("That did not work", exception.Message));

            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc cref="RunAsync{T}(Func{Task{T}})"/>
    private async Task<long?> RunAsync(Func<Task<long>> work)
    {
        IsBusy = true;

        try
        {
            return await Task.Run(work);
        }
        catch (Exception exception)
        {
            await _modalService.Show(ConfirmationDialogViewModel.Refusal("That did not work", exception.Message));

            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc cref="RunAsync{T}(Func{T})"/>
    private async Task<long?> RunAsync(Func<long> work)
    {
        IsBusy = true;

        try
        {
            return await Task.Run(work);
        }
        catch (Exception exception)
        {
            await _modalService.Show(ConfirmationDialogViewModel.Refusal("That did not work", exception.Message));

            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Puts one store operation on the shell strip for as long as it runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The strip is where all progress on this page goes</b>, rather than beside the buttons that
    /// started it. Verifying used to draw its own line and its own Stop under the store's row, which
    /// meant this page reported long work one way and the rest of the app another - and the row's
    /// version vanished the moment somebody navigated away from a pass that runs for minutes.
    /// </para>
    /// <para>
    /// It also covers the wait. Each of these claims the store exclusively - they all delete - so an
    /// apply wanting the same store queues behind them and, less obviously, they queue behind an apply.
    /// <see cref="Waiting"/> is what says so, and only when it is true.
    /// </para>
    /// </remarks>
    private IBackgroundTask Announce(string title, Action? cancel = null)
    {
        return _backgroundTasks.Begin(title, cancel: cancel);
    }

    /// <summary>
    /// The detail to show while this operation is queued behind something using the same store.
    /// </summary>
    /// <remarks>
    /// Handed to the maintenance call, which invokes it only where the claim could not be had at once
    /// - so the sentence appears exactly when it is the truth. Whatever the operation reports next
    /// replaces it, which for a verification pass is its first file.
    /// </remarks>
    private static Action Waiting(IBackgroundTask task)
    {
        return () => task.Report("Waiting for an apply to finish with this store");
    }

    private Task ReportAsync(string title, string message)
    {
        return _modalService.Show(ConfirmationDialogViewModel.Notice(title, message));
    }

    private void OnServingVolumeChanged(object? sender, EventArgs e)
    {
        RefreshStores();
        _navigationLockService.AcquireLock(this);
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// One row per store this machine has, rebuilt whenever an assignment changes. Rows are cached
    /// by volume so that pointing a disk elsewhere and back does not discard a path or size the user
    /// typed.
    /// </summary>
    /// <remarks>
    /// Stores that serve nothing are listed too, deliberately. A disk that used to hold a game keeps
    /// its cache until something removes it, and a store nothing points at is never swept - eviction
    /// only ever runs on the store a sync is using - so the page that can empty it is the only thing
    /// that will.
    /// </remarks>
    private void RefreshStores()
    {
        var settings = _settingsRepository.Settings;

        var servingVolumes = ModFolderVolumes
            .Select(x => x.ServingVolume)
            .Concat(settings.Stores.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Stores.Clear();

        foreach (var volume in servingVolumes)
        {
            if (!_storesByVolume.TryGetValue(volume, out var store))
            {
                var configured = settings.Stores.GetValueOrDefault(volume);

                store = new ContentStoreViewModel(
                    volume,
                    configured?.Path ?? ContentStoreSettings.GetDefaultPath(volume),
                    (configured?.MaxSizeBytes ?? ContentStoreSettings.DefaultMaxSizeBytes) / (double)_bytesPerGigabyte,
                    _dialogService);

                store.Modified += OnStoreModified;
                _storesByVolume[volume] = store;
            }

            store.Served = ModFolderVolumes
                .Where(x => string.Equals(x.ServingVolume, volume, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.VolumeRoot)
                .ToList();

            Stores.Add(store);
        }

        OnPropertyChanged(nameof(HasStores));
    }

    private void OnStoreModified(object? sender, EventArgs e)
    {
        _navigationLockService.AcquireLock(this);
        HasUnsavedChanges = true;
    }

    private List<string> GetValidationErrors()
    {
        var errors = new List<string>();

        foreach (var store in Stores)
        {
            if (string.IsNullOrWhiteSpace(store.Path))
            {
                errors.Add($"The store on {store.VolumeRoot} needs a folder.");
            }
            if (store.MaxSizeGigabytes <= 0)
            {
                errors.Add($"The store on {store.VolumeRoot} needs a maximum size.");
            }
        }

        if (string.IsNullOrWhiteSpace(ImageCache.Path))
        {
            errors.Add("The image cache needs a folder.");
        }
        if (ImageCache.MaxSizeGigabytes <= 0)
        {
            errors.Add("The image cache needs a maximum size.");
        }

        return errors;
    }

    private static IReadOnlyList<string> GetCandidateVolumes(IEnumerable<string> modFolderVolumes, ClientSettings settings)
    {
        var drives = DriveInfo.GetDrives()
            .Where(x => x.DriveType == DriveType.Fixed && x.IsReady)
            .Select(x => FileSystemHelper.NormalizeVolumeRoot(x.RootDirectory.FullName));

        return drives
            .Concat(modFolderVolumes)
            .Concat(settings.Stores.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
