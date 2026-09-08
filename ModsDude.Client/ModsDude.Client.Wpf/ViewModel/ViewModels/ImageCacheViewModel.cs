using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// The machine's image cache: where mod artwork is kept and how large it may grow. One of these,
/// not one per disk - images are copies rather than hardlinks, so nothing binds them to a volume.
/// </summary>
public partial class ImageCacheViewModel(
    string path,
    double maxSizeGigabytes,
    IDialogService dialogService)
    : ObservableObject
{
    public event EventHandler? Modified;

    [ObservableProperty]
    private string _path = path;

    [ObservableProperty]
    private double _maxSizeGigabytes = maxSizeGigabytes;

    /// <summary>What is on disk, once somebody has counted it. Null while that is still running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageSummary))]
    private ModImageCacheUsage? _usage;

    public string UsageSummary => Usage switch
    {
        null => "Measuring...",
        { Entries: 0 } => "Empty",
        _ => $"{ByteSize.Describe(Usage.TotalBytes)} in {Usage.Entries} images"
    };


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
