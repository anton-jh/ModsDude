using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Wpf.Diagnostics;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// The app-level notice for work that failed quietly: imagery that could not be uploaded or shown,
/// a row that could not finish loading.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on the drift notice and deliberately weaker than it. Drift risks a savegame, so it
/// argues for itself and offers to fix the problem; this one has nothing to offer but the truth -
/// something did not work, here is roughly how much of it, the log has the rest.
/// </para>
/// <para>
/// Aggregated rather than raised per failure, because the failures come in batches: one storage
/// container that does not exist is not 2,000 problems, it is one problem seen 2,000 times.
/// Dismissal starts a cooldown rather than silencing the session - counting continues while it
/// runs, and the next failure after it shows the full total.
/// </para>
/// </remarks>
public partial class BackgroundProblemViewModel(
    ILogger<BackgroundProblemViewModel> logger)
    : ObservableObject, IBackgroundProblemReporter
{
    /// <summary>
    /// Long enough that a dismissal means something during a long import, short enough that a
    /// problem which is still happening comes back before the user has finished the session.
    /// </summary>
    private static readonly TimeSpan _cooldown = TimeSpan.FromMinutes(10);

    private readonly Lock _lock = new();
    private readonly Dictionary<BackgroundProblem, int> _counts = [];

    private DateTimeOffset? _dismissedAt;


    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _headline = "";

    [ObservableProperty]
    private string _detail = "";


    public void Report(BackgroundProblem problem)
    {
        lock (_lock)
        {
            _counts[problem] = _counts.GetValueOrDefault(problem) + 1;
        }

        // Reported from decode threads, upload continuations and the import track; everything below
        // is bound.
        _ = Application.Current?.Dispatcher.InvokeAsync(Refresh);
    }


    [RelayCommand]
    private void OpenLog()
    {
        if (LogFolder.TryOpen(logger) is false)
        {
            // The one thing this notice can do, and it failed. Naming the folder is still better
            // than nothing, so the detail line becomes the fallback.
            Detail = $"The log is in {LogFolder.Path}.";
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        _dismissedAt = DateTimeOffset.UtcNow;

        IsVisible = false;
    }


    private void Refresh()
    {
        KeyValuePair<BackgroundProblem, int>[] snapshot;

        lock (_lock)
        {
            snapshot = [.. _counts.Where(x => x.Value > 0)];
        }

        if (snapshot.Length == 0 || InCooldown())
        {
            IsVisible = false;

            return;
        }

        Headline = snapshot.Length == 1
            ? DescribeHeadline(snapshot[0].Key)
            : "Some background work did not finish";

        Detail = string.Join(" ", snapshot.Select(x => Describe(x.Key, x.Value)))
            + " The log has the details.";

        IsVisible = true;
    }

    private bool InCooldown()
    {
        return _dismissedAt is DateTimeOffset dismissed
            && DateTimeOffset.UtcNow - dismissed < _cooldown;
    }


    private static string DescribeHeadline(BackgroundProblem problem) => problem switch
    {
        BackgroundProblem.ImageUpload => "Mod images could not be uploaded",
        BackgroundProblem.ImageDisplay => "Some images could not be shown",
        _ => "Some rows could not finish loading"
    };

    private static string Describe(BackgroundProblem problem, int count) => problem switch
    {
        // Said in terms of the consequence, because "an upload failed" means nothing to somebody
        // looking at a list of mods with initials where the pictures should be.
        BackgroundProblem.ImageUpload => count == 1
            ? "One mod's images could not be uploaded, so it shows initials for everybody until a client holding the file tries again."
            : $"{count} mods' images could not be uploaded, so they show initials for everybody until a client holding the files tries again.",

        BackgroundProblem.ImageDisplay => count == 1
            ? "1 image could not be shown and was replaced with initials."
            : $"{count} images could not be shown and were replaced with initials.",

        _ => count == 1
            ? "1 row could not finish loading."
            : $"{count} rows could not finish loading."
    };
}
