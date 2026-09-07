using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.Concurrent;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Turns one import's reports into the two things that show them: a bar on each row, and one line in
/// the shell.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per row, not per import.</b> At two thousand mods a single global spinner cannot tell a working
/// import from a hung one, which is why every row carries its own phase and its own upload bar.
/// </para>
/// <para>
/// <b>And per import as well</b>, because the row bars stop being visible the moment the user
/// navigates away - which they will, since the import is precisely the thing they do not have to wait
/// on. The shell strip is what survives that, and it counts <em>mods finished</em> rather than bytes:
/// bytes are the row's unit, and a bar that jumped between file sizes would say less than one that
/// walks.
/// </para>
/// <para>
/// Shared by the catalog page and the profile editor. They ran identical copies of this, which is one
/// copy too many for a class whose whole job is to agree with itself about what a percent is.
/// </para>
/// <para>
/// Byte counts arrive thousands of times per file, on whatever thread is doing the upload, so
/// anything finer than a whole percent is redraw nobody can see. WPF marshals the property changes
/// themselves, which is why this does not dispatch.
/// </para>
/// </remarks>
public sealed class ModImportRowProgress(
    IReadOnlyDictionary<ModVersionIdentity, ModListItemViewModel> rows,
    IBackgroundTask? task = null)
    : IProgress<ModImportProgress>
{
    private readonly ConcurrentDictionary<ModVersionIdentity, int> _lastPercent = new();

    /// <summary>Which versions have reached an outcome, so the shell's count is of mods and not of events.</summary>
    private readonly ConcurrentDictionary<ModVersionIdentity, byte> _finished = new();


    public void Report(ModImportProgress value)
    {
        ReportToShell(value);

        if (rows.TryGetValue(value.Identity, out var row) is false)
        {
            return;
        }

        if (value.Phase is ModImportPhase.Uploading && row.IsUploading)
        {
            var percent = value.TotalBytes > 0
                ? (int)(value.BytesTransferred * 100 / value.TotalBytes)
                : 0;

            if (_lastPercent.TryGetValue(value.Identity, out var last) && last == percent)
            {
                return;
            }

            _lastPercent[value.Identity] = percent;
        }

        row.Apply(value);
    }


    /// <summary>
    /// One line for the whole run: how many mods are done, and the name of one that is moving.
    /// </summary>
    /// <remarks>
    /// The three terminal phases all count, failures included. The strip says how far through the
    /// <em>run</em> is; what became of each mod is the dialog's and the rows' business, and a bar that
    /// stalled short because two mods failed would be reporting the wrong thing.
    /// </remarks>
    private void ReportToShell(ModImportProgress value)
    {
        if (task is null)
        {
            return;
        }

        if (value.Phase is ModImportPhase.Completed or ModImportPhase.Failed or ModImportPhase.Skipped)
        {
            _finished[value.Identity] = 0;
        }

        var name = rows.TryGetValue(value.Identity, out var row) ? row.Name : value.Identity.ModId.Value;

        task.Report($"{value.Phase}: {name}", _finished.Count, rows.Count);
    }
}
