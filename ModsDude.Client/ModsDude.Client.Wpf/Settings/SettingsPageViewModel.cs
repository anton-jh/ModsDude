using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Builds;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Startup;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Transfers;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell;
using ModsDude.Client.Wpf.Shell.BackgroundTasks;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;
using ModsDude.Client.Wpf.Shell.Updates;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace ModsDude.Client.Wpf.Settings;

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

    private readonly IClientSettingsRepository _settingsRepository;
    private readonly IContentStoreMaintenance _maintenance;
    private readonly IModImageCache _imageCache;
    private readonly INavigationLockService _navigationLockService;
    private readonly IModalService _modalService;
    private readonly IFilePickerService _filePickerService;
    private readonly IBackgroundTaskReporter _backgroundTasks;
    private readonly TransferLimits _transferLimits;
    private readonly IAutostartService _autostart;
    private readonly IAppUpdater _updater;
    private readonly IAppLifetime _lifetime;
    private readonly Dictionary<string, ContentStoreViewModel> _storesByVolume = [];

    /// <summary>
    /// The stores as they are actually configured on disk, keyed by volume, refreshed whenever they
    /// are measured. What a row displays and what a row's buttons act on are deliberately two
    /// different things - see the remarks on this class.
    /// </summary>
    private IReadOnlyDictionary<string, ContentStore> _configuredStores =
        new Dictionary<string, ContentStore>(StringComparer.OrdinalIgnoreCase);


    public SettingsPageViewModel(
        IClientSettingsRepository settingsRepository,
        IGameRepository gameRepository,
        IContentStoreMaintenance maintenance,
        IModImageCache imageCache,
        IFilePickerService filePickerService,
        IModalService modalService,
        INavigationLockService navigationLockService,
        IBackgroundTaskReporter backgroundTasks,
        TransferLimits transferLimits,
        IAutostartService autostart,
        IAppUpdater updater,
        IAppLifetime lifetime)
    {
        _lifetime = lifetime;
        _settingsRepository = settingsRepository;
        _maintenance = maintenance;
        _imageCache = imageCache;
        _filePickerService = filePickerService;
        _modalService = modalService;
        _navigationLockService = navigationLockService;
        _backgroundTasks = backgroundTasks;
        _transferLimits = transferLimits;
        _autostart = autostart;
        _updater = updater;
        _updater.Changed += OnUpdaterChanged;

        var settings = settingsRepository.Snapshot();

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
            filePickerService);
        ImageCache.Modified += OnStoreModified;

        // Straight into the fields, so loading the page does not count as an edit.
        _downloadLimit = DescribeLimit(settings.Transfers.DownloadBytesPerSecond);
        _uploadLimit = DescribeLimit(settings.Transfers.UploadBytesPerSecond);
        _closeToTray = settings.Background.CloseToTray;
        _notifyDrift = settings.Notifications.Drift;
        _notifyCheckInReminders = settings.Notifications.CheckInReminders;
        _notifyFinishedActions = settings.Notifications.FinishedActions;
        _notifyFriendActivity = settings.Notifications.FriendActivity;
        _startWithWindows = autostart.State is AutostartState.On;

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

    /// <summary>
    /// The download limit in Mbit/s, as typed. Text rather than a number, because empty is the
    /// ordinary value - no limit - and a numeric box has nowhere to put that.
    /// </summary>
    [ObservableProperty]
    private string _downloadLimit;

    /// <inheritdoc cref="DownloadLimit"/>
    [ObservableProperty]
    private string _uploadLimit;

    /// <summary>Whether closing the window hides it to the tray. See <see cref="BackgroundSettings"/>.</summary>
    [ObservableProperty]
    private bool _closeToTray;

    // Which Windows notifications may be sent while the window is not in front. See NotificationSettings.

    [ObservableProperty]
    private bool _notifyDrift;

    [ObservableProperty]
    private bool _notifyCheckInReminders;

    [ObservableProperty]
    private bool _notifyFinishedActions;

    [ObservableProperty]
    private bool _notifyFriendActivity;

    /// <summary>
    /// Whether the app starts with Windows. Read from Windows rather than from a setting of ours - see
    /// <see cref="AutostartService"/> - so this is what the registry said when the page was opened.
    /// </summary>
    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>The build this copy is, for the line above the update status.</summary>
    public string BuildText => $"Build {AppBuild.Description}";

    /// <summary>What the updater is doing, in a sentence.</summary>
    public string UpdateStatusText => _updater.StatusText;

    /// <summary>Whether there is a downloaded version to restart into.</summary>
    public bool IsUpdateReady => _updater.ReadyBuild is not null;

    /// <summary>Looks for an update now instead of at the next round. Only an installed copy has one to look for.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdates() => _updater.CheckAsync();

    private bool CanCheckForUpdates() => _updater.CanCheck;

    /// <summary>Restarts into the downloaded version. Asks first if something is still running.</summary>
    [RelayCommand]
    private async Task RestartToUpdate()
    {
        if (await ConfirmLeavingAsync("restart"))
        {
            await _updater.RestartAsync();
        }
    }

    [RelayCommand]
    private async Task Restart()
    {
        if (await ConfirmLeavingAsync("restart"))
        {
            await _lifetime.RestartAsync();
        }
    }

    [RelayCommand]
    private async Task Quit()
    {
        if (await ConfirmLeavingAsync("quit"))
        {
            await _lifetime.QuitAsync();
        }
    }

    /// <summary>Asks before unsaved settings are lost to the app leaving. True where there are none.</summary>
    private async Task<bool> ConfirmLeavingAsync(string leaving)
    {
        if (HasUnsavedChanges is false)
        {
            return true;
        }

        var modal = ConfirmationModalViewModel.ConfirmDiscardChanges(leaving);
        await _modalService.Show(modal);

        return modal.Result;
    }

    /// <summary>
    /// The updater reports from whichever thread it happened to be on; everything bound here is the UI's.
    /// </summary>
    private void OnUpdaterChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(UpdateStatusText));
            OnPropertyChanged(nameof(IsUpdateReady));
            CheckForUpdatesCommand.NotifyCanExecuteChanged();
        });
    }

    /// <summary>False for an install that may not register itself, which is the whole of a debug build.</summary>
    public bool IsAutostartAvailable => _autostart.IsAvailable;

    /// <summary>
    /// The line under the box: what it does, or why it cannot be ticked, or that Windows has it switched
    /// off - the one state where ticking and saving would otherwise look like it had worked and not.
    /// </summary>
    public string AutostartNote => _autostart.State switch
    {
        AutostartState.NotAvailable => IsAutostartAvailable is false && AppIdentity.IsProduction is false
            ? $"Not available in this build: only the installed copy starts with Windows, and this is {AppIdentity.DisplayName}."
            : "Not available: there is no installed copy of ModsDude to start.",
        AutostartState.DisabledInWindows =>
            "Windows has ModsDude switched off under Settings > Apps > Startup, so it is not starting. "
            + "Tick this and save to switch it back on.",
        _ => "It starts hidden in the tray, and opens when you click its icon."
    };

    public bool CanManage => HasUnsavedChanges is false && IsBusy is false;

    public string ManagementBlockedReason => HasUnsavedChanges
        ? "Save your changes to verify or reclaim a store - these act on the folders as they are saved."
        : string.Empty;


    [RelayCommand]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        var errors = GetValidationErrors();

        if (errors.Count > 0)
        {
            var modal = ConfirmationModalViewModel.ValidationErrors(errors);
            await _modalService.Show(modal);

            return;
        }

        await ApplyAutostartAsync();

        _settingsRepository.Update(settings =>
        {
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

            settings.Transfers.DownloadBytesPerSecond = ParseLimit(DownloadLimit);
            settings.Transfers.UploadBytesPerSecond = ParseLimit(UploadLimit);

            settings.Background.CloseToTray = CloseToTray;
            settings.Notifications.Drift = NotifyDrift;
            settings.Notifications.CheckInReminders = NotifyCheckInReminders;
            settings.Notifications.FinishedActions = NotifyFinishedActions;
            settings.Notifications.FriendActivity = NotifyFriendActivity;
        });

        // Into the live limiters as well as the file, so a download already running slows down - or
        // speeds up - from its next read rather than from the next start of the app.
        _transferLimits.Apply(_settingsRepository.Read(x => x.Transfers));
        _navigationLockService.ReleaseLock(this);

        HasUnsavedChanges = false;

        // A limit that has just come down is the third way a store ends up over it, and the only one
        // with a person watching. Before the measure, so what the rows then report is the size after
        // the trim rather than a number that shrinks a second later on its own.
        await _maintenance.SweepAllAsync(cancellationToken);

        // The stores may now be somewhere else or allowed to be a different size, so what was
        // measured a moment ago is about a different set of folders.
        await RefreshUsageAsync();
    }

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

        var confirmation = new ConfirmationModalViewModel(
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
            var report = await RunAsync(
                () => _maintenance.VerifyAsync(store, progress, cancellation.Token, Waiting(task)));

            await ReportVerificationAsync(row, report?.Value);
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

        var confirmation = new ConfirmationModalViewModel(
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

        var ran = await RunAsync(() => _maintenance.ReclaimAsync(store, CancellationToken.None, Waiting(task)));

        if (ran?.Value is not ContentStoreClearResult result)
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
    /// Sends the files sync rescued into this store to the Recycle Bin.
    /// </summary>
    /// <remarks>
    /// Asked about first: a quarantined file is precisely a mod that <em>no</em> repo registers, which
    /// is why it was moved rather than deleted.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanManage))]
    public async Task EmptyQuarantine(ContentStoreViewModel row)
    {
        if (FindStore(row) is not ContentStore store)
        {
            return;
        }

        var confirmation = new ConfirmationModalViewModel(
            "Move the rescued files to the Recycle Bin?",
            $"These are files sync found in a mod folder that no repo has registered, kept in {store.QuarantinePath}. "
                + "Nothing can fetch them back once the Recycle Bin is emptied.",
            IconKind.Warning,
            "Move them to the Recycle Bin",
            "Keep them");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        using var task = Announce($"Emptying the quarantine folder on {row.VolumeRoot}");

        var ran = await RunAsync(() => _maintenance.RecycleQuarantineAsync(store, CancellationToken.None, Waiting(task)));

        if (ran is null)
        {
            return;
        }

        if (ran.Value is long recycled)
        {
            await ReportAsync("Recycled", $"Moved {ByteSize.Describe(recycled)} to the Recycle Bin.");
        }
        else
        {
            await ReportAsync(
                "Not moved",
                $"The Recycle Bin would not take the files - {row.VolumeRoot} may not have one. They are still in {store.QuarantinePath}; remove them yourself if you no longer want them.");
        }

        await RefreshUsageAsync();
    }

    /// <summary>
    /// Empties the machine's image cache. Costs re-fetching thumbnails and nothing else, so it is
    /// the one of these that does not ask first.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanManage))]
    public async Task EmptyImageCache()
    {
        if (await RunAsync(_imageCache.Clear) is not { } reclaimed)
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
        _updater.Changed -= OnUpdaterChanged;

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
    /// failure into a modal rather than the app's error modal - a store that could not be swept is
    /// a full disk, not a broken client.
    /// </summary>
    /// <remarks>
    /// A cancellation is let straight through: stopping a verification pass on purpose is not a
    /// failure, and turning it into "that did not work" would call the user's own decision an error.
    /// </remarks>
    /// <returns>What the work answered, or null where it failed and the modal has said so.</returns>
    private async Task<Ran<T>?> RunAsync<T>(Func<Task<T>> work)
    {
        IsBusy = true;

        try
        {
            return new Ran<T>(await Task.Run(work));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _modalService.Show(ConfirmationModalViewModel.Refusal("That did not work", exception.Message));

            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<Ran<T>?> RunAsync<T>(Func<T> work) => await RunAsync(() => Task.FromResult(work()));

    private sealed record Ran<T>(T Value);

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
        return _modalService.Show(ConfirmationModalViewModel.Notice(title, message));
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
        var settings = _settingsRepository.Snapshot();

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
                    _filePickerService);

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

        AddLimitError(errors, "download", DownloadLimit);
        AddLimitError(errors, "upload", UploadLimit);

        return errors;
    }

    private static void AddLimitError(List<string> errors, string direction, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var megabits) is false ||
            megabits < TransferRate.MinimumMegabits)
        {
            errors.Add(
                $"The {direction} limit has to be a number of Mbit/s, at least {TransferRate.MinimumMegabits} - or empty for no limit.");
        }
    }

    /// <summary>Null for empty, which is no limit. Only called once validation has passed.</summary>
    private static long? ParseLimit(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? null
            : TransferRate.ToBytesPerSecond(double.Parse(text, NumberStyles.Float, CultureInfo.CurrentCulture));
    }

    private static string DescribeLimit(long? bytesPerSecond)
    {
        return bytesPerSecond is long rate
            ? TransferRate.ToMegabits(rate).ToString("0.#", CultureInfo.CurrentCulture)
            : string.Empty;
    }

    partial void OnDownloadLimitChanged(string value) => OnStoreModified(this, EventArgs.Empty);

    partial void OnUploadLimitChanged(string value) => OnStoreModified(this, EventArgs.Empty);

    partial void OnCloseToTrayChanged(bool value) => OnStoreModified(this, EventArgs.Empty);

    partial void OnNotifyDriftChanged(bool value) => OnStoreModified(this, EventArgs.Empty);

    partial void OnNotifyCheckInRemindersChanged(bool value) => OnStoreModified(this, EventArgs.Empty);

    partial void OnNotifyFinishedActionsChanged(bool value) => OnStoreModified(this, EventArgs.Empty);

    partial void OnNotifyFriendActivityChanged(bool value) => OnStoreModified(this, EventArgs.Empty);

    partial void OnStartWithWindowsChanged(bool value) => OnStoreModified(this, EventArgs.Empty);

    /// <summary>
    /// Writes the start-with-Windows choice to the registry, if it is a change.
    /// </summary>
    /// <remarks>
    /// <b>A refusal is reported, not thrown.</b> A locked-down machine can stop a user writing to their
    /// own Run key, and the rest of what was just saved is still saved - the box simply goes back to what
    /// Windows says, so the page does not go on claiming something that is not true.
    /// </remarks>
    private async Task ApplyAutostartAsync()
    {
        var isOn = _autostart.State is AutostartState.On;

        if (_autostart.IsAvailable is false || StartWithWindows == isOn)
        {
            return;
        }

        try
        {
            if (StartWithWindows)
            {
                _autostart.Enable();
            }
            else
            {
                _autostart.Disable();
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            await ReportAsync(
                "Could not change start with Windows",
                "Windows would not let ModsDude change its startup entry. Everything else was saved.");
        }

        StartWithWindows = _autostart.State is AutostartState.On;

        // The note reads the state, and the state just changed.
        OnPropertyChanged(nameof(AutostartNote));
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
