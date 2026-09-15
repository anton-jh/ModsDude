using CommunityToolkit.Mvvm.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One row under the strip's bar: a part of the running task that has been going long enough to be
/// worth naming on its own.
/// </summary>
/// <remarks>
/// Held across updates rather than rebuilt, so a bar that is filling is not replaced by an identical
/// bar starting again every tenth of a second. <see cref="Key"/> is what the strip matches them by.
/// </remarks>
public partial class BackgroundSubtaskViewModel(object key) : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    /// <summary>0 to 100, and meaningless while <see cref="IsIndeterminate"/> is true.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    /// <summary>The caller's own words for where it is - "41.2 MB / 68 MB". Null for most rows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAmount))]
    private string? _amount;


    public object Key { get; } = key;

    public bool HasAmount => string.IsNullOrWhiteSpace(Amount) is false;
}
