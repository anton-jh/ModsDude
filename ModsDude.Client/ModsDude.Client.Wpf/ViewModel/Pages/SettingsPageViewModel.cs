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
/// <b>Housekeeping acts on what is saved, not on what is typed</b>, so the buttons that sweep and
/// empty are disabled while there are unsaved edits. Otherwise "empty this store" would mean the
/// folder in the text box on a page where that folder has not been written down anywhere yet, which
/// is a good way to clear the wrong directory.
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
        LocalInstanceRepository localInstanceRepository,
        ContentStoreMaintenance maintenance,
        ModImageCache imageCache,
        IDialogService dialogService,
        IModalService modalService,
        NavigationLockService navigationLockService)
    {
        _settingsRepository = settingsRepository;
        _maintenance = maintenance;
        _imageCache = imageCache;
        _dialogService = dialogService;
        _modalService = modalService;
        _navigationLockService = navigationLockService;

        var settings = settingsRepository.Settings;

        // A store on a disk with no mod folders on it serves nothing, so the disks with instances on
        // them are what the page is about.
        var modFolderVolumes = localInstanceRepository.Instances
            .Select(x => x.ModFolder)
            .OfType<string>()
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
    [NotifyCanExecuteChangedFor(nameof(SweepStoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyStoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyQuarantineCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyImageCacheCommand))]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    [NotifyCanExecuteChangedFor(nameof(SweepStoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyStoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyQuarantineCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmptyImageCacheCommand))]
    private bool _isBusy;

    public bool CanManage => HasUnsavedChanges is false && IsBusy is false;

    public string ManagementBlockedReason => HasUnsavedChanges
        ? "Save your changes to sweep or empty a store - these act on the folders as they are saved."
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

        // The stores may now be somewhere else or allowed to be a different size, so what was
        // measured a moment ago is about a different set of folders.
        await RefreshUsageAsync();
    }

    /// <summary>
    /// Trims a store back inside its size limit, leaving what the mod folders it serves are running.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanManage))]
    public async Task SweepStore(ContentStoreViewModel row)
    {
        if (FindStore(row) is not ContentStore store)
        {
            return;
        }

        var result = await RunAsync(() => _maintenance.Sweep(store, CancellationToken.None));

        if (result is null)
        {
            return;
        }

        await ReportAsync(
            "Swept",
            result.EntriesEvicted == 0
                ? $"The store on {row.VolumeRoot} was already inside its limit. Nothing was removed."
                : $"Dropped {result.EntriesEvicted} files and freed {ByteSize.Describe(result.BytesReclaimed)}. "
                  + "Everything removed is registered in a repo, so it comes back on demand.");

        await RefreshUsageAsync();
    }

    /// <summary>
    /// Empties a store completely. Safe to offer, because everything in it is registered somewhere
    /// and therefore re-downloadable - the cost is bandwidth, never data.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanManage))]
    public async Task EmptyStore(ContentStoreViewModel row)
    {
        if (FindStore(row) is not ContentStore store)
        {
            return;
        }

        var confirmation = new ConfirmationDialogViewModel(
            $"Empty the store on {row.VolumeRoot}?",
            "Every mod file kept here is dropped. Nothing is lost - each one is registered in a repo and "
                + "downloads again when a profile needs it - but the next sync to a disk this store serves "
                + "will have to fetch what it needs.\n\nInstalled mod folders are not touched.",
            IconKind.Question,
            "Empty it",
            "Leave it");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        var result = await RunAsync(() => store.Clear(CancellationToken.None));

        if (result is null)
        {
            return;
        }

        await ReportAsync(
            "Emptied",
            $"Dropped {result.EntriesDeleted} files and freed {ByteSize.Describe(result.BytesReclaimed)}."
                + (result.Failed > 0
                    ? $"\n\n{result.Failed} could not be removed because something else is holding them open. They go on the next sweep."
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

        var reclaimed = await RunAsync(store.ClearQuarantine);

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
