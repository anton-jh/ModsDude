using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// Machine-wide settings. Today that is the content store: which store serves each disk holding mod
/// folders, where it lives and how large it may grow.
/// </summary>
public partial class SettingsPageViewModel
    : PageViewModel, IDisposable
{
    private const long _bytesPerGigabyte = 1024L * 1024 * 1024;
    private const double _defaultStoreSizeGigabytes = 100;

    private readonly ClientSettingsRepository _settingsRepository;
    private readonly NavigationLockService _navigationLockService;
    private readonly IModalService _modalService;
    private readonly IDialogService _dialogService;
    private readonly Dictionary<string, ContentStoreViewModel> _storesByVolume = [];


    public SettingsPageViewModel(
        ClientSettingsRepository settingsRepository,
        LocalInstanceRepository localInstanceRepository,
        IDialogService dialogService,
        IModalService modalService,
        NavigationLockService navigationLockService)
    {
        _settingsRepository = settingsRepository;
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

    public bool HasVolumes => ModFolderVolumes.Count > 0;
    public bool HasNoVolumes => ModFolderVolumes.Count == 0;


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

        _settingsRepository.Save();
        _navigationLockService.ReleaseLock(this);
    }

    public void Dispose()
    {
        foreach (var volume in ModFolderVolumes)
        {
            volume.ServingVolumeChanged -= OnServingVolumeChanged;
        }

        _navigationLockService.ReleaseLock(this);
    }


    private void OnServingVolumeChanged(object? sender, EventArgs e)
    {
        RefreshStores();
        _navigationLockService.AcquireLock(this);
    }

    /// <summary>
    /// One row per store that actually serves something, rebuilt whenever an assignment changes.
    /// Rows are cached by volume so that pointing a disk elsewhere and back does not discard a path
    /// or size the user typed.
    /// </summary>
    private void RefreshStores()
    {
        var settings = _settingsRepository.Settings;

        var servingVolumes = ModFolderVolumes
            .Select(x => x.ServingVolume)
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
                    configured?.Path ?? Path.Combine(volume, "ModsDude", "store"),
                    configured is null ? _defaultStoreSizeGigabytes : configured.MaxSizeBytes / (double)_bytesPerGigabyte,
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
    }

    private void OnStoreModified(object? sender, EventArgs e)
    {
        _navigationLockService.AcquireLock(this);
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
