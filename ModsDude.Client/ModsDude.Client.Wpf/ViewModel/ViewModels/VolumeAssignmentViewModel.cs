using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One disk holding mod folders, and the choice of which store serves it.
/// </summary>
public partial class VolumeAssignmentViewModel : ObservableObject
{
    public VolumeAssignmentViewModel(
        string volumeRoot,
        int instanceCount,
        string servingVolume,
        IReadOnlyList<string> candidateVolumes)
    {
        VolumeRoot = volumeRoot;
        InstanceCount = instanceCount;
        Options = candidateVolumes
            .Select(x => new ServingVolumeOption(x, DescribeOption(volumeRoot, x)))
            .ToList();
        _servingVolume = Options.Any(x => string.Equals(x.VolumeRoot, servingVolume, StringComparison.OrdinalIgnoreCase))
            ? servingVolume
            : volumeRoot;
    }


    public event EventHandler? ServingVolumeChanged;

    public string VolumeRoot { get; }
    public int InstanceCount { get; }
    public IReadOnlyList<ServingVolumeOption> Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TradeOff))]
    [NotifyPropertyChangedFor(nameof(IsServedByOwnStore))]
    private string _servingVolume;

    public string InstanceSummary => InstanceCount == 1
        ? "1 mod folder here"
        : $"{InstanceCount} mod folders here";

    /// <summary>
    /// Both sides of the choice, in the same words either way. A store on another disk is a
    /// deliberate trade of sync time for space, not a misconfiguration.
    /// </summary>
    public string TradeOff => IsServedByOwnStore
        ? "Mods are hardlinked into the mod folder, so installing costs nothing on top of the store " +
          "and switching profiles takes seconds. This disk holds the whole cache, which is the part that grows."
        : $"Mods are copied in from the store on {ServingVolume}, so this disk holds only the profile in use " +
          $"while the cache lives on {ServingVolume}. Every install and replace becomes a cross-disk copy, so syncing takes longer.";

    public bool IsServedByOwnStore => string.Equals(ServingVolume, VolumeRoot, StringComparison.OrdinalIgnoreCase);


    partial void OnServingVolumeChanged(string value)
    {
        ServingVolumeChanged?.Invoke(this, EventArgs.Empty);
    }


    private static string DescribeOption(string volumeRoot, string candidate)
    {
        return string.Equals(volumeRoot, candidate, StringComparison.OrdinalIgnoreCase)
            ? $"Its own store on {candidate} - hardlink, no extra space"
            : $"The store on {candidate} - copy, less space used here";
    }
}

public record ServingVolumeOption(string VolumeRoot, string Description);

/// <summary>One content store: where it lives, how large it may grow, and which disks it serves.</summary>
public partial class ContentStoreViewModel(
    string volumeRoot,
    string path,
    double maxSizeGigabytes,
    IDialogService dialogService)
    : ObservableObject
{
    public event EventHandler? Modified;

    public string VolumeRoot { get; } = volumeRoot;

    [ObservableProperty]
    private string _path = path;

    [ObservableProperty]
    private double _maxSizeGigabytes = maxSizeGigabytes;

    private IReadOnlyList<string> _served = [];
    public IReadOnlyList<string> Served
    {
        get => _served;
        set
        {
            _served = value;
            OnPropertyChanged(nameof(Served));
            OnPropertyChanged(nameof(ServedSummary));
        }
    }

    public string ServedSummary => $"Serves {string.Join(", ", Served)}";


    [RelayCommand]
    public void PickPath()
    {
        if (dialogService.PickFolder(string.IsNullOrWhiteSpace(Path) ? null : Path) is string folder)
        {
            Path = folder;
        }
    }


    partial void OnPathChanged(string value)
    {
        Modified?.Invoke(this, EventArgs.Empty);
    }

    partial void OnMaxSizeGigabytesChanged(double value)
    {
        Modified?.Invoke(this, EventArgs.Empty);
    }
}
