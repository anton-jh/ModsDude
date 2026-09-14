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
/// <para>
/// <b>Rows only.</b> The shell line belongs to whoever owns the strip entry, which is
/// <see cref="Services.ModImportCoordinator"/> - so a caller with no rows at all, such as a save
/// running on behalf of a page that has been navigated away from, still gets one.
/// </para>
/// </remarks>
public sealed class ModImportRowProgress(IReadOnlyDictionary<ModVersionIdentity, ModListItemViewModel> rows)
    : IProgress<ModImportProgress>
{
    private readonly ConcurrentDictionary<ModVersionIdentity, int> _lastPercent = new();


    public void Report(ModImportProgress value)
    {
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
}
