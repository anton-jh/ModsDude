using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Shared;

namespace ModsDude.Client.Wpf.Settings;

/// <summary>
/// One disk holding mod folders, and the choice of which store serves it.
/// </summary>
public partial class VolumeAssignmentViewModel : ObservableObject
{
    public VolumeAssignmentViewModel(
        string volumeRoot,
        int modFolderCount,
        string servingVolume,
        IReadOnlyList<string> candidateVolumes)
    {
        VolumeRoot = volumeRoot;
        ModFolderCount = modFolderCount;
        Options = candidateVolumes
            .Select(x => new ServingVolumeOption(x, DescribeOption(volumeRoot, x)))
            .ToList();
        _servingVolume = Options.Any(x => string.Equals(x.VolumeRoot, servingVolume, StringComparison.OrdinalIgnoreCase))
            ? servingVolume
            : volumeRoot;
    }


    public event EventHandler? ServingVolumeChanged;

    public string VolumeRoot { get; }
    public int ModFolderCount { get; }
    public IReadOnlyList<ServingVolumeOption> Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TradeOff))]
    [NotifyPropertyChangedFor(nameof(IsServedByOwnStore))]
    private string _servingVolume;

    public string ModFolderSummary => ModFolderCount == 1
        ? "1 mod folder"
        : $"{ModFolderCount} mod folders";

    /// <summary>
    /// Both sides of the choice, in the same words either way. A store on another disk is a
    /// deliberate trade of sync time for space, not a misconfiguration.
    /// </summary>
    public string TradeOff => IsServedByOwnStore
        ? "No extra space · fast sync"
        : "Less space used on this disk · slower sync";

    public bool IsServedByOwnStore => string.Equals(ServingVolume, VolumeRoot, StringComparison.OrdinalIgnoreCase);


    partial void OnServingVolumeChanged(string value)
    {
        ServingVolumeChanged?.Invoke(this, EventArgs.Empty);
    }


    private static string DescribeOption(string volumeRoot, string candidate)
    {
        return string.Equals(volumeRoot, candidate, StringComparison.OrdinalIgnoreCase)
            ? $"Own store on {candidate}"
            : $"Store on {candidate}";
    }
}

public record ServingVolumeOption(string VolumeRoot, string Description);

/// <summary>One content store: where it lives, how large it may grow, and which disks it serves.</summary>
public partial class ContentStoreViewModel(
    string volumeRoot,
    string path,
    double maxSizeGigabytes,
    IFilePickerService filePickerService)
    : ObservableObject
{
    public event EventHandler? Modified;

    public string VolumeRoot { get; } = volumeRoot;

    [ObservableProperty]
    private string _path = path;

    /// <summary>The cap, in the units somebody types. The app keeps the store within it.</summary>
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
        ? "Serves no mod folders"
        : $"Serves {string.Join(", ", Served)}";

    /// <summary>
    /// What is on disk, once somebody has measured it. Null while that is still being counted, which
    /// on a full store is a walk of tens of thousands of files.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageSummary))]
    [NotifyPropertyChangedFor(nameof(ReclaimLabel))]
    [NotifyPropertyChangedFor(nameof(ReclaimSummary))]
    [NotifyPropertyChangedFor(nameof(HasReclaimSummary))]
    [NotifyPropertyChangedFor(nameof(QuarantineSummary))]
    [NotifyPropertyChangedFor(nameof(HasQuarantine))]
    private ContentStoreUsage? _usage;

    /// <summary>What is on disk, in one line.</summary>
    public string UsageSummary => Usage switch
    {
        null => "Measuring…",
        { Entries: 0 } => "Empty",
        _ => $"{ByteSize.Describe(Usage.TotalBytes)} in {Usage.Entries} files"
    };

    /// <summary>
    /// Why the store's size and the space it would give back are two different numbers.
    /// </summary>
    /// <remarks>
    /// The amount is on the button (<see cref="ReclaimLabel"/>), so this says only the gap between it
    /// and the size above. That gap exists only on a hardlink-served disk; on a copy-served one this
    /// says nothing.
    /// </remarks>
    public string ReclaimSummary => Usage is { Entries: > 0 } usage && usage.TotalBytes > usage.ReclaimableBytes
        ? $"{ByteSize.Describe(usage.TotalBytes - usage.ReclaimableBytes)} in use by mod folders"
        : "";

    public bool HasReclaimSummary => ReclaimSummary.Length > 0;

    /// <summary>
    /// The emptying button's label, carrying the space it would actually give back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Falls back to the bare verb in the tidy state where the store holds exactly what is installed
    /// and nothing more, since "Reclaim 0 bytes" is a button that argues against itself.
    /// </para>
    /// </remarks>
    public string ReclaimLabel => Usage is { ReclaimableBytes: > 0 } usage
        ? $"Reclaim {ByteSize.Describe(usage.ReclaimableBytes)}"
        : "Reclaim space";

    public bool HasQuarantine => Usage?.QuarantineBytes > 0;

    /// <summary>
    /// The one part of a store that is not re-downloadable: files sync found in a mod folder that no
    /// repo registers, moved here because the Recycle Bin would not take them.
    /// </summary>
    public string QuarantineSummary => Usage is null
        ? string.Empty
        : $"Rescued files: {ByteSize.Describe(Usage.QuarantineBytes)} · not in any repo";


    [RelayCommand]
    public void PickPath()
    {
        if (filePickerService.PickFolder(string.IsNullOrWhiteSpace(Path) ? null : Path) is string folder)
        {
            // A folder that already holds other things gets the store in a folder of its own inside
            // it: the store deletes files, and must never be pointed at somebody's Documents.
            Path = ContentStore.CanBeRootAt(folder)
                ? folder
                : System.IO.Path.Combine(folder, "ModsDude store");
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
