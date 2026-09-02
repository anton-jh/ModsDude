using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>One line of the plan preview: what is about to happen to one mod, and why.</summary>
/// <remarks>
/// <para>
/// The mod is rendered by <see cref="ModListItemViewModel"/>, exactly as on the repo's mod list and
/// on a profile's - same icon, same version chip, same name that opens the details dialog. What
/// hangs off the end of the row is the one thing this page knows that they do not: what applying the
/// profile would do to this file.
/// </para>
/// <para>
/// Only ever built for an item that changes something. <see cref="ModSyncAction.Keep"/> is a count in
/// the summary rather than a row: on a re-apply it is nearly the whole list, and a page of lines
/// saying nothing will happen buries the few that say something will.
/// </para>
/// </remarks>
public class ModSyncRowViewModel
{
    public ModSyncRowViewModel(ModSyncItem item, ModListItemViewModel listItem)
    {
        Item = listItem;

        // Nothing here picks mods, and a plan preview offers no action on the repo.
        Item.IsSelectable = false;

        Action = item.Action;

        ActionText = item.Action switch
        {
            ModSyncAction.Install => "Install",
            ModSyncAction.Replace => "Replace",
            ModSyncAction.UninstallRecoverable => "Uninstall",
            ModSyncAction.Quarantine => "Move to Recycle Bin",
            _ => item.Action.ToString()
        };

        Detail = item.Action switch
        {
            ModSyncAction.Install => "Not in the mod folder yet.",
            ModSyncAction.Replace when item.InstalledIsRecoverable =>
                $"Replaces {item.InstalledVersion?.Value}. The old file stays recoverable.",
            ModSyncAction.Replace =>
                $"Replaces {item.InstalledVersion?.Value}, which the repo has never seen - so that file goes to the Recycle Bin.",
            ModSyncAction.UninstallRecoverable =>
                "Not in this profile. The repo has it, so it can be installed again at any time.",
            ModSyncAction.Quarantine =>
                "Not in this profile and not in the repo, so nothing else has a copy. It goes to the Recycle Bin, not deleted.",
            _ => string.Empty
        };
    }


    /// <summary>The shared list row - the mod as it is rendered anywhere else in the app.</summary>
    public ModListItemViewModel Item { get; }

    /// <summary>Bound rather than only rendered: the chip's colour is a trigger on this.</summary>
    public ModSyncAction Action { get; }

    public string ActionText { get; }

    /// <summary>
    /// The consequence, not the mechanism. A file heading for the Recycle Bin is the one the user has
    /// to be able to recognise, so it says so on the row rather than only in the confirmation. The
    /// version being installed is on the row's own chip, so nothing here repeats it.
    /// </summary>
    public string Detail { get; }
}
