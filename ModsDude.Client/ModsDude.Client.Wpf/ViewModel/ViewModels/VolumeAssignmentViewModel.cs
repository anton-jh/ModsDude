using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Sync;
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

    /// <summary>
    /// A store with nothing to serve is listed rather than hidden. It is the one somebody is most
    /// likely to want emptied - a disk that used to hold a game and now holds only its cache.
    /// </summary>
    public string ServedSummary => Served.Count == 0
        ? "Not serving any mod folder right now"
        : $"Serves {string.Join(", ", Served)}";

    /// <summary>
    /// What is on disk, once somebody has measured it. Null while that is still being counted, which
    /// on a full store is a walk of tens of thousands of files.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageSummary))]
    [NotifyPropertyChangedFor(nameof(QuarantineSummary))]
    [NotifyPropertyChangedFor(nameof(HasQuarantine))]
    private ContentStoreUsage? _usage;

    /// <summary>
    /// Both numbers, because they differ for a reason worth showing: what the store holds, and what
    /// emptying it would actually give back. On a hardlink-served disk the second is the smaller,
    /// since an entry the mod folder also names costs no bytes of its own.
    /// </summary>
    public string UsageSummary => Usage switch
    {
        null => "Measuring...",
        { Entries: 0 } => "Empty",
        { TotalBytes: var total, ReclaimableBytes: var free } when total == free
            => $"{ByteSize.Describe(total)} in {Usage.Entries} files",
        _ => $"{ByteSize.Describe(Usage.TotalBytes)} in {Usage.Entries} files, {ByteSize.Describe(Usage.ReclaimableBytes)} of it reclaimable"
    };

    public bool HasQuarantine => Usage?.QuarantineBytes > 0;

    /// <summary>
    /// Said in full, because this is the one part of a store that is not re-downloadable: these are
    /// files sync found in a mod folder that no repo registers, moved here because the Recycle Bin
    /// would not take them.
    /// </summary>
    public string QuarantineSummary => Usage is null
        ? string.Empty
        : $"{ByteSize.Describe(Usage.QuarantineBytes)} of rescued files that sync could not recycle. "
          + "Nothing in the repo holds these, so deleting them is the one thing here that cannot be undone.";


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
