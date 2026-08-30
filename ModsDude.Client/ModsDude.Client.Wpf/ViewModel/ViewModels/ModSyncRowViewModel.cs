using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>One line of the plan preview: what is about to happen to one mod, and why.</summary>
public class ModSyncRowViewModel(ModSyncItem item)
{
    public string Name { get; } = item.DisplayName;

    public string ModId { get; } = item.ModId.Value;

    public bool Locked { get; } = item.Locked;

    public string Action { get; } = item.Action switch
    {
        ModSyncAction.Keep => "Already correct",
        ModSyncAction.Install => "Install",
        ModSyncAction.Replace => "Replace",
        ModSyncAction.UninstallRecoverable => "Uninstall",
        ModSyncAction.Quarantine => "Move to Recycle Bin",
        _ => item.Action.ToString()
    };

    /// <summary>
    /// The consequence, not the mechanism. A file heading for the Recycle Bin is the one the user has
    /// to be able to recognise, so it says so on the row rather than only in the confirmation.
    /// </summary>
    public string Detail { get; } = item.Action switch
    {
        ModSyncAction.Keep => "The file already matches what the profile pins.",
        ModSyncAction.Install => $"Version {item.DesiredVersion?.Value}.",
        ModSyncAction.Replace when item.InstalledIsRecoverable =>
            $"{item.InstalledVersion?.Value} is replaced by {item.DesiredVersion?.Value}. The old file stays recoverable.",
        ModSyncAction.Replace =>
            $"{item.InstalledVersion?.Value} is replaced by {item.DesiredVersion?.Value}. The repo has never seen the installed file, so it goes to the Recycle Bin.",
        ModSyncAction.UninstallRecoverable =>
            "Not in this profile. The repo has it, so it can be installed again at any time.",
        ModSyncAction.Quarantine =>
            "Not in this profile and not registered in the repo, so nothing else has a copy. It goes to the Recycle Bin rather than being deleted.",
        _ => string.Empty
    };
}
